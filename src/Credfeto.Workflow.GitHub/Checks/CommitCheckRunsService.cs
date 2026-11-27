using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Json;

namespace Credfeto.Workflow.GitHub.Checks;

public sealed class CommitCheckRunsService : ICommitCheckRunsService
{
    // The check run statuses GitHub uses for a run that has not finished
    private static readonly string[] RunningStatuses = ["queued", "in_progress", "waiting", "pending", "requested"];

    private readonly IGitHubRestClient _restClient;

    public CommitCheckRunsService(IGitHubRestClient restClient)
    {
        this._restClient = restClient;
    }

    public ValueTask<CommitCheckRunsResponse> ReadAsync(
        string repository,
        string sha,
        CancellationToken cancellationToken
    )
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));
        string validSha = GitHubNames.CommitSha(value: sha, parameterName: nameof(sha));

        return this.ReadOrFailAsync(repository: validRepository, sha: validSha, cancellationToken: cancellationToken);
    }

    private async ValueTask<CommitCheckRunsResponse> ReadOrFailAsync(
        string repository,
        string sha,
        CancellationToken cancellationToken
    )
    {
        try
        {
            IReadOnlyList<CheckRunResponse?> runs = await this._restClient.GetAllPagesAsync(
                relativeUri: $"repos/{repository}/commits/{sha}/check-runs?per_page=100&filter=latest",
                pageTypeInfo: GitHubJsonContext.Default.CheckRunsPageResponse,
                items: page => page.CheckRuns,
                cancellationToken: cancellationToken
            );

            IReadOnlyList<CommitCheckRun> checkRuns = [.. runs.Select(ToCheckRun)];

            return new(Repo: repository, Sha: sha, Running: checkRuns.Count(IsRunning), CheckRuns: checkRuns);
        }
        catch (HttpRequestException exception)
            when (exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity)
        {
            throw new CommitNotFoundException(
                message: $"the commit {sha} was not found in {repository}",
                innerException: exception
            );
        }
        catch (Exception exception) when (exception is GitHubInvalidResponseException or HttpRequestException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new PrChecksUnreadableException(
                message: $"could not read the check runs of {repository} commit {sha}",
                innerException: exception
            );
        }
    }

    // Only a run with no conclusion at all is still going, whatever its status says
    private static bool IsRunning(CommitCheckRun run)
    {
        return run.Conclusion is null && RunningStatuses.Contains(value: run.Status, comparer: StringComparer.Ordinal);
    }

    private static CommitCheckRun ToCheckRun(CheckRunResponse? run)
    {
        if (run is not { Id: { } id, Name: { Length: > 0 } name, Status: { } status })
        {
            throw new GitHubInvalidResponseException("GitHub returned a check run without an id, name or status");
        }

        return new(
            Id: id,
            Name: UpstreamText.ReplaceLineBreaking(name),
            Status: UpstreamText.ReplaceLineBreaking(status),
            Conclusion: run.Conclusion is null ? null : UpstreamText.ReplaceLineBreaking(run.Conclusion),
            AppId: run.App?.Id
        );
    }
}
