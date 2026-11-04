using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Branches;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Json;
using Credfeto.Workflow.GitHub.Validation;

namespace Credfeto.Workflow.GitHub.PullRequests;

public sealed partial class PrCreateService : IPrCreateService
{
    private const string FORK_FLAG = "fork";

    private const int REGEX_TIMEOUT_MILLISECONDS = 1000;

    // Reading a status only needs the workflow state names to recognise one held in the board's built-in Status field;
    // the option ids are never written through this field
    private static readonly WorkflowStatusField StateNames = new(
        FieldId: "workflow-states",
        FieldName: "Status",
        OptionIds: WorkflowStates.All.ToDictionary(
            keySelector: state => state,
            elementSelector: state => state,
            comparer: StringComparer.Ordinal
        )
    );

    private static readonly int DevelopmentIndex = IndexOfState(WorkflowStates.DEVELOPMENT);

    private readonly IBoardWriter _boardWriter;
    private readonly IWorkflowBoardReader _boardReader;
    private readonly IWorkflowBoardResolver _boardResolver;
    private readonly ILeakChecker _leakChecker;
    private readonly IPullRequestListService _pullRequestList;
    private readonly IGitHubRestClient _restClient;
    private readonly IWorkflowStatusResolver _statusResolver;

    public PrCreateService(
        IGitHubRestClient restClient,
        ILeakChecker leakChecker,
        IWorkflowBoardResolver boardResolver,
        IWorkflowStatusResolver statusResolver,
        IWorkflowBoardReader boardReader,
        IBoardWriter boardWriter,
        IPullRequestListService pullRequestList
    )
    {
        this._restClient = restClient;
        this._leakChecker = leakChecker;
        this._boardResolver = boardResolver;
        this._statusResolver = statusResolver;
        this._boardReader = boardReader;
        this._boardWriter = boardWriter;
        this._pullRequestList = pullRequestList;
    }

    public ValueTask<PrCreateResponse> CreateAsync(
        string repository,
        string head,
        string? baseBranch,
        string title,
        string body,
        CancellationToken cancellationToken
    )
    {
        // Every argument is validated before the first GitHub call, so invalid input never costs one and is reported
        // as such rather than as a GitHub failure
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));
        string validHead = GitHubNames.Branch(value: head, parameterName: nameof(head));
        string? validBase = baseBranch is null
            ? null
            : ValidBase(
                head: validHead,
                baseBranch: GitHubNames.Branch(value: baseBranch, parameterName: nameof(baseBranch))
            );

        // GitHubNames.Repository guarantees exactly one separator between a non-empty owner and name
        int separator = validRepository.IndexOf('/', StringComparison.Ordinal);

        NewPullRequest pullRequest = new(
            Repository: validRepository,
            Owner: validRepository[..separator],
            Name: validRepository[(separator + 1)..],
            Head: validHead,
            Base: validBase,
            Title: ValidTitle(title),
            Body: ValidBody(body)
        );

        return this.CreateValidAsync(pullRequest: pullRequest, cancellationToken: cancellationToken);
    }

    private static string ValidBase(string head, string baseBranch)
    {
        return StringComparer.Ordinal.Equals(x: head, y: baseBranch)
            ? throw Usage(parameterName: nameof(baseBranch), message: "--base and --head must be different branches")
            : baseBranch;
    }

    private static string ValidTitle(string title)
    {
        if (!TitleValidator.IsValidTitle(title))
        {
            throw Usage(parameterName: nameof(title), message: TitleValidator.INVALID_TITLE_MESSAGE);
        }

        return TitleValidator.IsValidPullRequestTitle(title)
            ? title
            : throw Usage(parameterName: nameof(title), message: TitleValidator.INVALID_PULL_REQUEST_TITLE_MESSAGE);
    }

    private static string ValidBody(string body)
    {
        // Whitespace and invisible characters alone would show as an empty body on GitHub
        return TitleValidator.HasVisibleText(body)
            ? body
            : throw Usage(parameterName: nameof(body), message: "the body is empty");
    }

    // The message is fixed text naming the rule the value broke, never the rejected value itself
    private static ParameterValidationException Usage(string parameterName, string message)
    {
        return new(message) { ParameterName = parameterName, Rule = message };
    }

    private async ValueTask<PrCreateResponse> CreateValidAsync(
        NewPullRequest pullRequest,
        CancellationToken cancellationToken
    )
    {
        await this.CheckLeaksAsync(pullRequest: pullRequest, cancellationToken: cancellationToken);

        // The board is resolved before anything is written, so a repository with no Workflow board fails here and
        // leaves no pull request behind
        WorkflowProject project = await this._boardResolver.ResolveAsync(
            repository: pullRequest.Repository,
            cancellationToken: cancellationToken
        );

        ResolvedStatusOption option = await this._statusResolver.ResolveAsync(
            repository: pullRequest.Repository,
            project: project,
            state: WorkflowStates.DEVELOPMENT,
            cancellationToken: cancellationToken
        );

        BoardTarget board = new(Project: project, Option: option);

        // An open pull request for the head is reused as it is, so its base is never resolved or compared
        ExistingPullRequest? existing = await this.FindOpenAsync(
            pullRequest: pullRequest,
            cancellationToken: cancellationToken
        );

        if (existing is { } found)
        {
            return await this.ExistingAsync(
                pullRequest: pullRequest,
                existing: found,
                board: board,
                cancellationToken: cancellationToken
            );
        }

        string baseBranch =
            pullRequest.Base
            ?? await this.DefaultBaseAsync(pullRequest: pullRequest, cancellationToken: cancellationToken);

        await this.EnsureHeadExistsAsync(pullRequest: pullRequest, cancellationToken: cancellationToken);

        return await this.CreateNewAsync(
            pullRequest: pullRequest with
            {
                Base = baseBranch,
            },
            board: board,
            cancellationToken: cancellationToken
        );
    }

    private async ValueTask CheckLeaksAsync(NewPullRequest pullRequest, CancellationToken cancellationToken)
    {
        ThrowIfRefused(this._leakChecker.CheckBody(pullRequest.Body));
        ThrowIfRefused(
            await this._leakChecker.CheckRepositoryReferencesAsync(
                repository: pullRequest.Repository,
                subject: LeakSubject.BODY,
                text: pullRequest.Body,
                cancellationToken: cancellationToken
            )
        );

        // A title gets the same rules as a body except its length, which the usage checks already limit
        ThrowIfRefused(this._leakChecker.CheckValue(subject: LeakSubject.TITLE, value: pullRequest.Title));
        ThrowIfRefused(
            await this._leakChecker.CheckRepositoryReferencesAsync(
                repository: pullRequest.Repository,
                subject: LeakSubject.TITLE,
                text: pullRequest.Title,
                cancellationToken: cancellationToken
            )
        );
    }

    // The refused text is never echoed back: only the subject, the rule and, for a body, the line are reported
    private static void ThrowIfRefused(LeakRefusal? refusal)
    {
        if (refusal is null)
        {
            return;
        }

        string subject = refusal.Subject == LeakSubject.BODY ? "body" : "title";
        string rule = RuleName(refusal.Rule);
        int? lineNumber = refusal.Subject == LeakSubject.BODY ? refusal.LineNumber : null;
        string line = lineNumber is { } number
            ? $" (line {number.ToString(CultureInfo.InvariantCulture)})"
            : string.Empty;

        throw new PrCreateRefusedException($"{subject} refused: {rule}{line}")
        {
            Subject = subject,
            Rule = rule,
            LineNumber = lineNumber,
        };
    }

    private static string RuleName(LeakRule rule)
    {
        return rule switch
        {
            LeakRule.TOKEN => "token",
            LeakRule.HOST_PATH => "host path",
            LeakRule.CONTROL_CHARACTER => "control character",
            LeakRule.INVALID_UTF8 => "invalid UTF-8",
            LeakRule.TOO_LONG => "too long",
            _ => "private repository",
        };
    }

    private async ValueTask<string> DefaultBaseAsync(NewPullRequest pullRequest, CancellationToken cancellationToken)
    {
        string defaultBranch = await this.DefaultBranchAsync(
            repository: pullRequest.Repository,
            cancellationToken: cancellationToken
        );

        return StringComparer.Ordinal.Equals(x: defaultBranch, y: pullRequest.Head)
            ? throw new PrCreateFailedException($"the --head is the default branch of {pullRequest.Repository}")
            : defaultBranch;
    }

    private async ValueTask<string> DefaultBranchAsync(string repository, CancellationToken cancellationToken)
    {
        try
        {
            RepositoryResponse response = await this._restClient.GetAsync(
                relativeUri: $"repos/{repository}",
                responseTypeInfo: GitHubJsonContext.Default.RepositoryResponse,
                cancellationToken: cancellationToken
            );

            return ValidDefaultBranch(response.DefaultBranch);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new RepositoryNotFoundException(
                message: $"the repository {repository} was not found, or the caller cannot see it",
                innerException: exception
            );
        }
        catch (Exception exception) when (IsUpstreamFailure(exception))
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new PrCreateFailedException(
                message: $"could not read the default branch of {repository}",
                innerException: exception
            );
        }
    }

    private static string ValidDefaultBranch(string? branch)
    {
        if (string.IsNullOrEmpty(branch))
        {
            throw new GitHubInvalidResponseException("GitHub returned a repository without a default branch");
        }

        try
        {
            return GitHubNames.Branch(value: branch, parameterName: nameof(branch));
        }
        catch (ParameterValidationException exception)
        {
            throw new GitHubInvalidResponseException(
                message: "GitHub returned an invalid default branch",
                innerException: exception
            );
        }
    }

    private async ValueTask EnsureHeadExistsAsync(NewPullRequest pullRequest, CancellationToken cancellationToken)
    {
        BranchResponse response;

        try
        {
            response = await this._restClient.GetAsync(
                relativeUri: $"repos/{pullRequest.Repository}/branches/{GitHubPath.BranchPath(pullRequest.Head)}",
                responseTypeInfo: GitHubJsonContext.Default.BranchResponse,
                cancellationToken: cancellationToken
            );
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            // The head has passed the usage checks only, not the leak checks, so it is not named
            throw new PrCreateFailedException(
                message: $"the --head was not found in {pullRequest.Repository}: push the branch first",
                innerException: exception
            );
        }
        catch (Exception exception) when (IsUpstreamFailure(exception))
        {
            throw new PrCreateFailedException(
                message: $"could not read the --head in {pullRequest.Repository}",
                innerException: exception
            );
        }

        // GitHub follows a renamed branch, so a different name means the head as given does not exist
        if (!StringComparer.Ordinal.Equals(x: response.Name, y: pullRequest.Head))
        {
            throw new PrCreateFailedException($"unexpected result for the --head in {pullRequest.Repository}");
        }
    }

    // A fork's branch can share the head's name, so only a pull request from the repository itself is a match
    private async ValueTask<ExistingPullRequest?> FindOpenAsync(
        NewPullRequest pullRequest,
        CancellationToken cancellationToken
    )
    {
        PullRequestListResponse response = await this._pullRequestList.ListAsync(
            repository: pullRequest.Repository,
            issue: null,
            head: pullRequest.Head,
            labels: null,
            cancellationToken: cancellationToken
        );

        IReadOnlyList<PullRequestListEntry> matches =
        [
            .. response.PullRequests.Where(entry =>
                StringComparer.Ordinal.Equals(x: entry.Head, y: pullRequest.Head)
                && !entry.Flags.Contains(value: FORK_FLAG, comparer: StringComparer.Ordinal)
            ),
        ];

        return matches.Count switch
        {
            0 => null,
            1 => Existing(repository: pullRequest.Repository, entry: matches[0]),
            _ => throw new PrCreateFailedException(
                $"more than one open pull request exists for the --head in {pullRequest.Repository}"
            ),
        };
    }

    // The URL is returned to the caller, so one GitHub listed in an unexpected shape is never passed on
    private static ExistingPullRequest Existing(string repository, PullRequestListEntry entry)
    {
        return
            CreatedItemUrl.TryParse(pattern: PullRequestUrl(), url: entry.Url.ToString(), out Uri? uri, out int number)
            && number == entry.Number
            ? new(Url: uri, Number: number, Base: entry.Base)
            : throw new PrCreateFailedException(
                $"unexpected result for the open pull request for the --head in {repository}"
            );
    }

    private async ValueTask<PrCreateResponse> ExistingAsync(
        NewPullRequest pullRequest,
        ExistingPullRequest existing,
        BoardTarget board,
        CancellationToken cancellationToken
    )
    {
        await this.AdvanceExistingAsync(
            pullRequest: pullRequest,
            existing: existing,
            board: board,
            cancellationToken: cancellationToken
        );

        return new(
            Url: existing.Url,
            Created: false,
            Note: $"an open pull request already exists for the --head (base {existing.Base}); its title and body were not changed"
        );
    }

    // An existing pull request is only moved forward to Development, never back from a later review state
    private async ValueTask AdvanceExistingAsync(
        NewPullRequest pullRequest,
        ExistingPullRequest existing,
        BoardTarget board,
        CancellationToken cancellationToken
    )
    {
        const string suffix = "the pull request already exists";

        BoardItemStatus status;

        try
        {
            status = await this._boardReader.ReadStatusAsync(
                owner: pullRequest.Owner,
                repo: pullRequest.Name,
                kind: WorkItemKind.PULL_REQUEST,
                number: existing.Number,
                project: board.Project,
                field: StateNames,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception) when (IsBoardFailure(exception))
        {
            throw BoardFailure(url: existing.Url, suffix: suffix, exception: exception);
        }

        if (!NeedsDevelopment(status: status, url: existing.Url, suffix: suffix))
        {
            return;
        }

        await this.PutOnBoardAsync(
            pullRequest: pullRequest,
            number: existing.Number,
            url: existing.Url,
            board: board,
            suffix: suffix,
            cancellationToken: cancellationToken
        );
    }

    // A status the board holds under a name that is not a workflow state cannot be placed before or after Development,
    // so it is reported rather than overwritten
    private static bool NeedsDevelopment(BoardItemStatus status, Uri url, string suffix)
    {
        if (status.State != BoardItemState.STATE)
        {
            return true;
        }

        int index = IndexOfState(status.Name);

        return index >= 0
            ? index < DevelopmentIndex
            : throw new PrCreateBoardWriteException(
                $"the Workflow board status of the pull request is not a known workflow state; {suffix}: {url}"
            )
            {
                Url = url,
            };
    }

    private static int IndexOfState(string? name)
    {
        for (int index = 0; index < WorkflowStates.All.Count; ++index)
        {
            if (StringComparer.Ordinal.Equals(x: WorkflowStates.All[index], y: name))
            {
                return index;
            }
        }

        return -1;
    }

    private async ValueTask<PrCreateResponse> CreateNewAsync(
        NewPullRequest pullRequest,
        BoardTarget board,
        CancellationToken cancellationToken
    )
    {
        PullRequestCreateResponse response;

        try
        {
            response = await this._restClient.PostAsync(
                relativeUri: $"repos/{pullRequest.Repository}/pulls",
                body: new PullRequestCreateRequest(
                    Title: pullRequest.Title,
                    Head: pullRequest.Head,
                    Base: pullRequest.Base ?? string.Empty,
                    Body: pullRequest.Body,
                    Draft: true
                ),
                requestTypeInfo: GitHubJsonContext.Default.PullRequestCreateRequest,
                responseTypeInfo: GitHubJsonContext.Default.PullRequestCreateResponse,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception) when (IsUpstreamFailure(exception))
        {
            return await this.RecoverAsync(
                pullRequest: pullRequest,
                board: board,
                failure: exception,
                cancellationToken: cancellationToken
            );
        }

        CreatedPullRequest created = Created(response.HtmlUrl ?? string.Empty);

        await this.PutOnBoardAsync(
            pullRequest: pullRequest,
            number: created.Number,
            url: created.Url,
            board: board,
            suffix: "the pull request was already created",
            cancellationToken: cancellationToken
        );

        return new(Url: created.Url, Created: true, Note: null);
    }

    // A failed create may still have opened the pull request, or another caller may have opened one for the same head
    // meanwhile, so the open pull requests are listed again before the failure is reported
    private async ValueTask<PrCreateResponse> RecoverAsync(
        NewPullRequest pullRequest,
        BoardTarget board,
        Exception failure,
        CancellationToken cancellationToken
    )
    {
        ExistingPullRequest? existing;

        try
        {
            existing = await this.FindOpenAsync(pullRequest: pullRequest, cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is PullRequestListFailedException or PrCreateFailedException)
        {
            existing = null;
        }

        if (existing is not { } found)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new PrCreateFailedException(
                message: $"could not create the pull request in {pullRequest.Repository}",
                innerException: failure
            );
        }

        return await this.ExistingAsync(
            pullRequest: pullRequest,
            existing: found,
            board: board,
            cancellationToken: cancellationToken
        );
    }

    private static CreatedPullRequest Created(string url)
    {
        if (CreatedItemUrl.TryParse(pattern: PullRequestUrl(), url: url, out Uri? uri, out int number))
        {
            return new(Url: uri, Number: number);
        }

        // The pull request may exist, so what GitHub returned is named, on one line, rather than lost
        throw new PrCreateFailedException(
            $"unexpected result for the new pull request, which may have been created: {UpstreamText.ReplaceLineBreaking(url)}"
        );
    }

    // The status is never read back: the writer already fails on any response that does not confirm the change
    private async ValueTask PutOnBoardAsync(
        NewPullRequest pullRequest,
        int number,
        Uri url,
        BoardTarget board,
        string suffix,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await this._boardWriter.SetItemStatusAsync(
                owner: pullRequest.Owner,
                repo: pullRequest.Name,
                kind: WorkItemKind.PULL_REQUEST,
                number: number,
                project: board.Project,
                fieldId: board.Option.FieldId,
                fieldName: board.Option.FieldName,
                optionId: board.Option.OptionId,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception) when (IsBoardFailure(exception))
        {
            throw BoardFailure(url: url, suffix: suffix, exception: exception);
        }
    }

    private static PrCreateBoardWriteException BoardFailure(Uri url, string suffix, Exception exception)
    {
        string reason =
            exception is BoardWriteException ? exception.Message : $"could not put {url} on the Workflow board";

        return new(message: $"{reason}; {suffix}: {url}", innerException: exception) { Url = url };
    }

    private static bool IsBoardFailure(Exception exception)
    {
        return exception is BoardWriteException or BoardReadFailedException or GitHubGraphQlException
            || IsUpstreamFailure(exception);
    }

    private static bool IsUpstreamFailure(Exception exception)
    {
        return exception is GitHubInvalidResponseException or HttpRequestException;
    }

    [GeneratedRegex(
        pattern: @"^https://[A-Za-z0-9.-]+(:[0-9]+)?/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/pull/(?<number>[0-9]+)\z",
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: REGEX_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex PullRequestUrl();

    [StructLayout(LayoutKind.Auto)]
    [DebuggerDisplay("{Repository}: {Head} -> {Base}: {Title}")]
    private readonly record struct NewPullRequest(
        string Repository,
        string Owner,
        string Name,
        string Head,
        string? Base,
        string Title,
        string Body
    );

    [StructLayout(LayoutKind.Auto)]
    [DebuggerDisplay("{Option.OptionId}")]
    private readonly record struct BoardTarget(WorkflowProject Project, ResolvedStatusOption Option);

    [StructLayout(LayoutKind.Auto)]
    [DebuggerDisplay("{Url}")]
    private readonly record struct CreatedPullRequest(Uri Url, int Number);

    [StructLayout(LayoutKind.Auto)]
    [DebuggerDisplay("{Url}")]
    private readonly record struct ExistingPullRequest(Uri Url, int Number, string Base);
}
