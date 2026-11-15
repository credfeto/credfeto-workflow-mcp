using System;
using System.Collections.Generic;
using System.Globalization;
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

namespace Credfeto.Workflow.GitHub.PullRequests;

public sealed class PrDiffService : IPrDiffService
{
    private const string DIFF_MEDIA_TYPE = "application/vnd.github.diff";

    private const string UNCHANGED_STATUS = "-";

    private readonly IGitHubRestClient _restClient;

    public PrDiffService(IGitHubRestClient restClient)
    {
        this._restClient = restClient;
    }

    public async ValueTask<PrDiffResult> DiffAsync(
        string repository,
        int pullRequest,
        string? mode,
        CancellationToken cancellationToken
    )
    {
        string validRepository;
        PrDiffMode validMode;

        try
        {
            validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));

            if (pullRequest <= 0)
            {
                throw ParameterValidationException.For(parameterName: nameof(pullRequest), rule: "must be above 0");
            }

            validMode = ParseMode(mode);
        }
        catch (ParameterValidationException exception)
        {
            return Failed(outcome: PrDiffOutcome.UsageError, message: exception.Message, mode: null);
        }

        string target = $"{validRepository}#{pullRequest.ToString(CultureInfo.InvariantCulture)}";

        try
        {
            return validMode == PrDiffMode.Diff
                ? await this.ReadDiffAsync(
                    validRepository: validRepository,
                    pullRequest: pullRequest,
                    target: target,
                    cancellationToken: cancellationToken
                )
                : await this.ReadFilesAsync(
                    validRepository: validRepository,
                    pullRequest: pullRequest,
                    mode: validMode,
                    target: target,
                    cancellationToken: cancellationToken
                );
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return Failed(
                outcome: PrDiffOutcome.NotFound,
                message: $"the pull request {target} was not found, or cannot be seen",
                mode: validMode
            );
        }
        catch (Exception exception) when (exception is GitHubInvalidResponseException or HttpRequestException)
        {
            return Failed(
                outcome: PrDiffOutcome.RuntimeError,
                message: $"could not read the diff of {target}",
                mode: validMode
            );
        }
    }

    private async ValueTask<PrDiffResult> ReadDiffAsync(
        string validRepository,
        int pullRequest,
        string target,
        CancellationToken cancellationToken
    )
    {
        string diff = await this._restClient.GetTextAsync(
            relativeUri: $"repos/{validRepository}/pulls/{pullRequest.ToString(CultureInfo.InvariantCulture)}",
            mediaType: DIFF_MEDIA_TYPE,
            cancellationToken: cancellationToken
        );

        return new(
            Outcome: PrDiffOutcome.Success,
            Message: $"read the diff of {target}",
            Mode: PrDiffMode.Diff,
            Diff: UpstreamText.ReplaceLineBreakingKeepingLayout(diff),
            Files: null
        );
    }

    private async ValueTask<PrDiffResult> ReadFilesAsync(
        string validRepository,
        int pullRequest,
        PrDiffMode mode,
        string target,
        CancellationToken cancellationToken
    )
    {
        // GitHub lists the files in the order the diff holds them
        IReadOnlyList<PullRequestFileResponse?> files = await this._restClient.GetAllPagesAsync(
            relativeUri: $"repos/{validRepository}/pulls/{pullRequest.ToString(CultureInfo.InvariantCulture)}/files?per_page=100",
            pageTypeInfo: GitHubJsonContext.Default.PullRequestFileResponseArray,
            cancellationToken: cancellationToken
        );

        return new(
            Outcome: PrDiffOutcome.Success,
            Message: $"read the changed files of {target}",
            Mode: mode,
            Diff: null,
            Files: [.. files.Select(file => mode == PrDiffMode.NameOnly ? NameOnly(file) : Stat(file))]
        );
    }

    private static PrDiffFile NameOnly(PullRequestFileResponse? file)
    {
        return new(Status: StatusLetter(file?.Status), Path: Path(file), Additions: null, Deletions: null);
    }

    // A binary file has no lines, so GitHub gives it 0 additions and 0 deletions
    private static PrDiffFile Stat(PullRequestFileResponse? file)
    {
        return file is { Additions: >= 0 and int additions, Deletions: >= 0 and int deletions }
            ? new(Status: null, Path: Path(file), Additions: additions, Deletions: deletions)
            : throw new GitHubInvalidResponseException("GitHub returned a changed file with no line counts");
    }

    // A renamed or copied file is named by its new path only
    private static string Path(PullRequestFileResponse? file)
    {
        return file?.FileName is { Length: > 0 } fileName
            ? UpstreamText.ReplaceLineBreaking(fileName)
            : throw new GitHubInvalidResponseException("GitHub returned a changed file with no name");
    }

    private static string StatusLetter(string? status)
    {
        return status switch
        {
            "added" => "A",
            "modified" => "M",
            "removed" => "D",
            "renamed" => "R",
            "copied" => "C",
            "changed" or "unchanged" => UNCHANGED_STATUS,
            _ => throw new GitHubInvalidResponseException("GitHub returned a changed file with an unknown status"),
        };
    }

    private static PrDiffMode ParseMode(string? mode)
    {
        return mode switch
        {
            null or "" or "diff" => PrDiffMode.Diff,
            "name-only" => PrDiffMode.NameOnly,
            "stat" => PrDiffMode.Stat,
            _ => throw ParameterValidationException.For(
                parameterName: nameof(mode),
                rule: "it must be diff, name-only or stat"
            ),
        };
    }

    private static PrDiffResult Failed(PrDiffOutcome outcome, string message, PrDiffMode? mode)
    {
        return new(Outcome: outcome, Message: message, Mode: mode, Diff: null, Files: null);
    }
}
