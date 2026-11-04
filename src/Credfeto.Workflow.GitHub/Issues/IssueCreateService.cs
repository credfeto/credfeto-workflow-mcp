using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Json;
using Credfeto.Workflow.GitHub.Labels;
using Credfeto.Workflow.GitHub.Leaks;
using Credfeto.Workflow.GitHub.Validation;

namespace Credfeto.Workflow.GitHub.Issues;

public sealed partial class IssueCreateService : IIssueCreateService
{
    private const string ALREADY_EXISTS = "already_exists";

    private const string LABELS_PARAMETER = "labels";

    private const string NUMBER_GROUP = "number";

    private const int REGEX_TIMEOUT_MILLISECONDS = 1000;

    private readonly IBoardWriter _boardWriter;
    private readonly IWorkflowBoardResolver _boardResolver;
    private readonly ILeakChecker _leakChecker;
    private readonly IGitHubRestClient _restClient;
    private readonly IWorkflowStatusResolver _statusResolver;

    public IssueCreateService(
        IGitHubRestClient restClient,
        ILeakChecker leakChecker,
        IWorkflowBoardResolver boardResolver,
        IWorkflowStatusResolver statusResolver,
        IBoardWriter boardWriter
    )
    {
        this._restClient = restClient;
        this._leakChecker = leakChecker;
        this._boardResolver = boardResolver;
        this._statusResolver = statusResolver;
        this._boardWriter = boardWriter;
    }

    public ValueTask<IssueCreateResponse> CreateAsync(
        string repository,
        string priority,
        string title,
        string body,
        IReadOnlyList<string>? labels,
        CancellationToken cancellationToken
    )
    {
        // Every argument is validated before the first GitHub call, so invalid input never costs one and is reported
        // as such rather than as a GitHub failure
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));
        string validPriority = ValidPriority(priority);

        // GitHubNames.Repository guarantees exactly one separator between a non-empty owner and name
        int separator = validRepository.IndexOf(value: '/', comparisonType: StringComparison.Ordinal);
        string owner = validRepository[..separator];

        NewIssue issue = new(
            Repository: validRepository,
            Owner: owner,
            Name: validRepository[(separator + 1)..],
            Title: ValidTitle(owner: owner, title: title),
            Body: ValidBody(body),
            Labels: IssueLabels(priority: validPriority, labels: labels ?? [])
        );

        return this.CreateValidAsync(issue: issue, cancellationToken: cancellationToken);
    }

    private static string ValidPriority(string priority)
    {
        if (string.IsNullOrWhiteSpace(priority))
        {
            throw Usage(
                parameterName: nameof(priority),
                message: $"--priority is required ({StandardLabels.PriorityList})"
            );
        }

        return StandardLabels.FindPriority(priority)
            ?? throw Usage(
                parameterName: nameof(priority),
                message: $"unknown --priority (expected {StandardLabels.PriorityList})"
            );
    }

    private static string ValidTitle(string owner, string title)
    {
        if (!TitleValidator.IsValidTitle(title))
        {
            throw Usage(parameterName: nameof(title), message: TitleValidator.INVALID_TITLE_MESSAGE);
        }

        return TitleValidator.IsValidIssueTitle(owner: owner, title: title)
            ? title
            : throw Usage(parameterName: nameof(title), message: TitleValidator.INVALID_ISSUE_TITLE_MESSAGE);
    }

    private static string ValidBody(string body)
    {
        // Whitespace and invisible characters alone would show as an empty body on GitHub
        return TitleValidator.HasVisibleText(body)
            ? body
            : throw Usage(parameterName: nameof(body), message: "the body is empty");
    }

    // The priority comes first, then each label in its standard spelling when it is a standard one, with a repeat in
    // any case dropped
    private static IReadOnlyList<string> IssueLabels(string priority, IReadOnlyList<string> labels)
    {
        List<string> issueLabels = [priority];
        HashSet<string> seen = [with(StringComparer.OrdinalIgnoreCase), priority];

        foreach (string label in labels.Select(ValidLabel))
        {
            if (StandardLabels.FindPriority(label) is { } canonical)
            {
                // The label is one of the priority names, so echoing it back cannot leak anything
                throw Usage(
                    parameterName: nameof(labels),
                    message: $"'{label}' is a priority; use --priority {canonical}, not --label"
                );
            }

            string name = StandardLabels.FindStandard(label) ?? label;

            if (seen.Add(name))
            {
                issueLabels.Add(name);
            }
        }

        return issueLabels;
    }

    private static string ValidLabel(string label)
    {
        string validLabel = GitHubNames.Label(value: label, parameterName: LABELS_PARAMETER);

        return TitleValidator.HasVisibleText(validLabel)
            ? validLabel
            : throw ParameterValidationException.For(
                parameterName: LABELS_PARAMETER,
                rule: "it must contain a visible character"
            );
    }

    // The message is fixed text naming the rule the value broke, never the rejected value itself
    private static ParameterValidationException Usage(string parameterName, string message)
    {
        return new(message) { ParameterName = parameterName, Rule = message };
    }

    private async ValueTask<IssueCreateResponse> CreateValidAsync(NewIssue issue, CancellationToken cancellationToken)
    {
        await this.CheckLeaksAsync(issue: issue, cancellationToken: cancellationToken);

        // The board is resolved before anything is written, so a repository with no Workflow board fails here and
        // leaves no issue behind
        WorkflowProject project = await this._boardResolver.ResolveAsync(
            repository: issue.Repository,
            cancellationToken: cancellationToken
        );

        ResolvedStatusOption option = await this._statusResolver.ResolveAsync(
            repository: issue.Repository,
            project: project,
            state: WorkflowStates.NOT_STARTED,
            cancellationToken: cancellationToken
        );

        await this.EnsureLabelsExistAsync(issue: issue, cancellationToken: cancellationToken);

        CreatedIssue created = await this.CreateIssueAsync(issue: issue, cancellationToken: cancellationToken);

        await this.PutOnBoardAsync(
            issue: issue,
            created: created,
            project: project,
            option: option,
            cancellationToken: cancellationToken
        );

        return new(Url: created.Url);
    }

    private async ValueTask CheckLeaksAsync(NewIssue issue, CancellationToken cancellationToken)
    {
        ThrowIfRefused(this._leakChecker.CheckBody(issue.Body));
        ThrowIfRefused(
            await this._leakChecker.CheckRepositoryReferencesAsync(
                repository: issue.Repository,
                subject: LeakSubject.BODY,
                text: issue.Body,
                cancellationToken: cancellationToken
            )
        );

        await this.CheckValueAsync(
            repository: issue.Repository,
            subject: LeakSubject.TITLE,
            value: issue.Title,
            cancellationToken: cancellationToken
        );

        // The priority is one of five fixed names, so only the labels the caller gave are checked
        foreach (string label in issue.Labels.Skip(1))
        {
            await this.CheckValueAsync(
                repository: issue.Repository,
                subject: LeakSubject.LABEL,
                value: label,
                cancellationToken: cancellationToken
            );
        }
    }

    // A title or a label gets the same rules as a body except its length, which the usage checks already limit
    private async ValueTask CheckValueAsync(
        string repository,
        LeakSubject subject,
        string value,
        CancellationToken cancellationToken
    )
    {
        ThrowIfRefused(this._leakChecker.CheckValue(subject: subject, value: value));
        ThrowIfRefused(
            await this._leakChecker.CheckRepositoryReferencesAsync(
                repository: repository,
                subject: subject,
                text: value,
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

        string subject = SubjectName(refusal.Subject);
        string rule = LeakRuleNames.Name(refusal.Rule);
        int? lineNumber = refusal.Subject == LeakSubject.BODY ? refusal.LineNumber : null;
        string line = lineNumber is { } number
            ? $" (line {number.ToString(CultureInfo.InvariantCulture)})"
            : string.Empty;

        throw new IssueCreateRefusedException($"{subject} refused: {rule}{line}")
        {
            Subject = subject,
            Rule = rule,
            LineNumber = lineNumber,
        };
    }

    private static string SubjectName(LeakSubject subject)
    {
        return subject switch
        {
            LeakSubject.BODY => "body",
            LeakSubject.TITLE => "title",
            _ => "label",
        };
    }

    private async ValueTask EnsureLabelsExistAsync(NewIssue issue, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> existing = await this.RepositoryLabelsAsync(
            repository: issue.Repository,
            cancellationToken: cancellationToken
        );

        HashSet<string> present = [with(StringComparer.OrdinalIgnoreCase), .. existing];

        foreach (string label in issue.Labels.Where(label => !present.Contains(label)))
        {
            await this.CreateLabelAsync(
                repository: issue.Repository,
                label: label,
                cancellationToken: cancellationToken
            );
        }
    }

    private async ValueTask<IReadOnlyList<string>> RepositoryLabelsAsync(
        string repository,
        CancellationToken cancellationToken
    )
    {
        try
        {
            IReadOnlyList<IssueLabelResponse?> labels = await this._restClient.GetAllPagesAsync(
                relativeUri: $"repos/{repository}/labels?per_page=100",
                pageTypeInfo: GitHubJsonContext.Default.IssueLabelResponseArray,
                cancellationToken: cancellationToken
            );

            return
            [
                .. labels.Select(label =>
                    label?.Name ?? throw new GitHubInvalidResponseException("GitHub returned a label without a name")
                ),
            ];
        }
        catch (Exception exception) when (IsUpstreamFailure(exception))
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new IssueCreateFailedException(
                message: $"could not read the labels of {repository}",
                innerException: exception
            );
        }
    }

    private async ValueTask CreateLabelAsync(string repository, string label, CancellationToken cancellationToken)
    {
        StandardLabel? standard = StandardLabels.FindDefinition(label);

        try
        {
            await this._restClient.PostAsync(
                relativeUri: $"repos/{repository}/labels",
                body: new LabelCreateRequest(Name: label, Color: standard?.Colour, Description: standard?.Description),
                requestTypeInfo: GitHubJsonContext.Default.LabelCreateRequest,
                responseTypeInfo: GitHubJsonContext.Default.IssueLabelResponse,
                cancellationToken: cancellationToken
            );
        }
        catch (HttpRequestException exception)
            when (GitHubHttpFailure.HasErrorCode(exception: exception, code: ALREADY_EXISTS))
        {
            // The label was created by someone else between the listing and this call, which is what was wanted
        }
        catch (Exception exception) when (IsUpstreamFailure(exception))
        {
            // The label has passed the usage and leak checks, so it is safe to name; the upstream detail stays in the
            // inner exception for diagnostics
            throw new IssueCreateFailedException(
                message: $"could not create the label '{label}' in {repository}",
                innerException: exception
            );
        }
    }

    private async ValueTask<CreatedIssue> CreateIssueAsync(NewIssue issue, CancellationToken cancellationToken)
    {
        IssueResponse response;

        try
        {
            response = await this._restClient.PostAsync(
                relativeUri: $"repos/{issue.Repository}/issues",
                body: new IssueCreateRequest(Title: issue.Title, Body: issue.Body, Labels: issue.Labels),
                requestTypeInfo: GitHubJsonContext.Default.IssueCreateRequest,
                responseTypeInfo: GitHubJsonContext.Default.IssueResponse,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception) when (IsUpstreamFailure(exception))
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new IssueCreateFailedException(
                message: $"could not create the issue in {issue.Repository}",
                innerException: exception
            );
        }

        return Created(response.HtmlUrl ?? string.Empty);
    }

    // Neither the host nor the repository in the URL is compared with anything: GitHub may be reached through a proxy
    // that answers with its own host, and a renamed or transferred repository answers with its new name
    private static CreatedIssue Created(string url)
    {
        Match match = IssueUrl().Match(url);

        if (
            match.Success
            && int.TryParse(
                s: match.Groups[NUMBER_GROUP].ValueSpan,
                style: NumberStyles.None,
                provider: CultureInfo.InvariantCulture,
                result: out int number
            )
            && number > 0
            && Uri.TryCreate(uriString: url, uriKind: UriKind.Absolute, result: out Uri? uri)
        )
        {
            return new(Url: uri, Number: number);
        }

        // The issue may exist, so what GitHub returned is named, on one line, rather than lost
        throw new IssueCreateFailedException(
            $"unexpected result for the new issue, which may have been created: {UpstreamText.ReplaceLineBreaking(url)}"
        );
    }

    // The status is never read back: the writer already fails on any response that does not confirm the change
    private async ValueTask PutOnBoardAsync(
        NewIssue issue,
        CreatedIssue created,
        WorkflowProject project,
        ResolvedStatusOption option,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await this._boardWriter.SetItemStatusAsync(
                owner: issue.Owner,
                repo: issue.Name,
                kind: WorkItemKind.ISSUE,
                number: created.Number,
                project: project,
                fieldId: option.FieldId,
                fieldName: option.FieldName,
                optionId: option.OptionId,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception)
            when (exception
                    is BoardWriteException
                        or GitHubGraphQlException
                        or GitHubInvalidResponseException
                        or HttpRequestException
            )
        {
            // A board write failure is fixed text; anything else may carry upstream detail, so it gets a fixed message
            string reason = exception is BoardWriteException
                ? exception.Message
                : $"could not put {created.Url} on the Workflow board";

            throw new IssueCreateBoardWriteException(
                message: $"{reason}; the issue was already created: {created.Url}",
                innerException: exception
            )
            {
                Url = created.Url,
            };
        }
    }

    private static bool IsUpstreamFailure(Exception exception)
    {
        return exception is GitHubInvalidResponseException or HttpRequestException;
    }

    [GeneratedRegex(
        pattern: @"^https://[A-Za-z0-9.-]+(:[0-9]+)?/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/issues/(?<number>[0-9]+)\z",
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: REGEX_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex IssueUrl();

    [StructLayout(LayoutKind.Auto)]
    [DebuggerDisplay("{Repository}: {Title}")]
    private readonly record struct NewIssue(
        string Repository,
        string Owner,
        string Name,
        string Title,
        string Body,
        IReadOnlyList<string> Labels
    );

    [StructLayout(LayoutKind.Auto)]
    [DebuggerDisplay("{Url}")]
    private readonly record struct CreatedIssue(Uri Url, int Number);
}
