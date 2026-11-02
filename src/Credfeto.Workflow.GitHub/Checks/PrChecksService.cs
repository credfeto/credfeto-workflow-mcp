using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Branches;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Json;

namespace Credfeto.Workflow.GitHub.Checks;

public sealed class PrChecksService : IPrChecksService
{
    private const int COMMIT_SHA_LENGTH = 40;

    private static readonly SearchValues<char> LowerHexDigits = SearchValues.Create("0123456789abcdef");

    private readonly IRequiredChecksReader _requiredChecksReader;
    private readonly IGitHubRestClient _restClient;

    public PrChecksService(IGitHubRestClient restClient, IRequiredChecksReader requiredChecksReader)
    {
        this._restClient = restClient;
        this._requiredChecksReader = requiredChecksReader;
    }

    public ValueTask<PrChecksResponse> ReadAsync(
        string repository,
        int? pullRequest,
        string? sha,
        string? branch,
        bool all,
        CancellationToken cancellationToken
    )
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));
        CheckTarget target = ValidTarget(pullRequest: pullRequest, sha: sha, branch: branch);

        return this.ReadOrFailAsync(
            repository: validRepository,
            target: target,
            all: all,
            cancellationToken: cancellationToken
        );
    }

    // The target is validated before anything is read, so an invalid one is reported as such rather than as unreadable
    // checks
    private static CheckTarget ValidTarget(int? pullRequest, string? sha, string? branch)
    {
        int given = (pullRequest is null ? 0 : 1) + (sha is null ? 0 : 1) + (branch is null ? 0 : 1);

        if (given != 1)
        {
            throw ParameterValidationException.For(
                parameterName: "pr",
                rule: "exactly one of pr, sha or branch must be given"
            );
        }

        if (pullRequest is { } number)
        {
            return number > 0
                ? new(PullRequest: number, Sha: null, Branch: null, Description: $"pull request #{number}")
                : throw ParameterValidationException.For(parameterName: nameof(pullRequest), rule: "must be above 0");
        }

        if (sha is not null)
        {
            string validSha = GitHubNames.CommitSha(sha, nameof(sha));

            return new(PullRequest: null, Sha: validSha, Branch: null, Description: $"commit {validSha}");
        }

        string validBranch = GitHubNames.Branch(value: branch!, parameterName: nameof(branch));

        return new(PullRequest: null, Sha: null, Branch: validBranch, Description: $"branch {validBranch}");
    }

    private async ValueTask<PrChecksResponse> ReadOrFailAsync(
        string repository,
        CheckTarget target,
        bool all,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await this.ReadChecksAsync(
                repository: repository,
                target: target,
                all: all,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception) when (exception is GitHubInvalidResponseException or HttpRequestException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new PrChecksUnreadableException(
                message: $"could not read the checks of {repository} {target.Description}",
                innerException: exception
            );
        }
    }

    private async ValueTask<PrChecksResponse> ReadChecksAsync(
        string repository,
        CheckTarget target,
        bool all,
        CancellationToken cancellationToken
    )
    {
        ResolvedHead head = await this.ResolveHeadAsync(
            repository: repository,
            target: target,
            cancellationToken: cancellationToken
        );

        IReadOnlyList<RequiredCheck> required = await this._requiredChecksReader.ReadRequiredChecksAsync(
            repository: repository,
            branch: head.RulesBranch,
            cancellationToken: cancellationToken
        );

        IReadOnlyList<CheckRunResponse?> runs = await this.ReadCheckRunsAsync(
            repository: repository,
            sha: head.Sha,
            cancellationToken: cancellationToken
        );

        IReadOnlyList<CommitStatusResponse?> statuses = await this._restClient.GetAllPagesAsync(
            relativeUri: $"repos/{repository}/commits/{head.Sha}/status?per_page=100",
            pageTypeInfo: GitHubJsonContext.Default.CombinedStatusResponse,
            items: page => page.Statuses,
            cancellationToken: cancellationToken
        );

        return PrChecksEvaluator.Evaluate(
            repository: repository,
            sha: head.Sha,
            required: required,
            runs: runs,
            statuses: statuses,
            all: all
        );
    }

    private ValueTask<ResolvedHead> ResolveHeadAsync(
        string repository,
        CheckTarget target,
        CancellationToken cancellationToken
    )
    {
        if (target.PullRequest is { } pullRequest)
        {
            return this.ResolvePullRequestAsync(
                repository: repository,
                pullRequest: pullRequest,
                cancellationToken: cancellationToken
            );
        }

        return target.Sha is { } sha
            ? this.ResolveCommitAsync(repository: repository, sha: sha, cancellationToken: cancellationToken)
            : this.ResolveBranchAsync(
                repository: repository,
                branch: target.Branch!,
                cancellationToken: cancellationToken
            );
    }

    // A pull request is judged by the rules of the branch it merges into
    private async ValueTask<ResolvedHead> ResolvePullRequestAsync(
        string repository,
        int pullRequest,
        CancellationToken cancellationToken
    )
    {
        PullRequestHeadResponse response;

        try
        {
            response = await this._restClient.GetAsync(
                relativeUri: $"repos/{repository}/pulls/{pullRequest}",
                responseTypeInfo: GitHubJsonContext.Default.PullRequestHeadResponse,
                cancellationToken: cancellationToken
            );
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new PullRequestNotFoundException(
                message: $"pull request {repository}#{pullRequest} was not found",
                innerException: exception
            );
        }

        return new(Sha: ValidHead(response.Head?.Sha), RulesBranch: ValidRulesBranch(response.Base?.Ref));
    }

    // A bare commit belongs to no branch, so it is judged by the rules of the default branch
    private async ValueTask<ResolvedHead> ResolveCommitAsync(
        string repository,
        string sha,
        CancellationToken cancellationToken
    )
    {
        RepositoryResponse response;

        try
        {
            response = await this._restClient.GetAsync(
                relativeUri: $"repos/{repository}",
                responseTypeInfo: GitHubJsonContext.Default.RepositoryResponse,
                cancellationToken: cancellationToken
            );
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new RepositoryNotFoundException(
                message: $"the repository {repository} was not found, or the caller cannot see it",
                innerException: exception
            );
        }

        return new(Sha: sha, RulesBranch: ValidRulesBranch(response.DefaultBranch));
    }

    private async ValueTask<ResolvedHead> ResolveBranchAsync(
        string repository,
        string branch,
        CancellationToken cancellationToken
    )
    {
        BranchInfo info = await this._requiredChecksReader.ReadBranchAsync(
            repository: repository,
            branch: branch,
            cancellationToken: cancellationToken
        );

        return new(Sha: ValidHead(info.Sha), RulesBranch: branch);
    }

    // GitHub reports commit ids in lower case, so anything else is a response this service cannot trust
    private static string ValidHead(string? sha)
    {
        return sha is { Length: COMMIT_SHA_LENGTH } && !sha.AsSpan().ContainsAnyExcept(LowerHexDigits)
            ? sha
            : throw new GitHubInvalidResponseException("GitHub returned an invalid head commit");
    }

    private static string ValidRulesBranch(string? branch)
    {
        if (string.IsNullOrEmpty(branch))
        {
            throw new GitHubInvalidResponseException("GitHub returned no branch to read the required checks of");
        }

        try
        {
            return GitHubNames.Branch(value: branch, parameterName: nameof(branch));
        }
        catch (ParameterValidationException exception)
        {
            throw new GitHubInvalidResponseException(
                message: "GitHub returned an invalid branch to read the required checks of",
                innerException: exception
            );
        }
    }

    private async ValueTask<IReadOnlyList<CheckRunResponse?>> ReadCheckRunsAsync(
        string repository,
        string sha,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await this._restClient.GetAllPagesAsync(
                relativeUri: $"repos/{repository}/commits/{sha}/check-runs?per_page=100&filter=all",
                pageTypeInfo: GitHubJsonContext.Default.CheckRunsPageResponse,
                items: page => page.CheckRuns,
                cancellationToken: cancellationToken
            );
        }
        catch (HttpRequestException exception)
            when (exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity)
        {
            throw new CommitNotFoundException(
                message: $"the commit {sha} was not found in {repository}",
                innerException: exception
            );
        }
    }

    [DebuggerDisplay("{Description}")]
    private readonly record struct CheckTarget(int? PullRequest, string? Sha, string? Branch, string Description);

    [DebuggerDisplay("{Sha} {RulesBranch}")]
    private readonly record struct ResolvedHead(string Sha, string RulesBranch);
}
