using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;

namespace Credfeto.Workflow.GitHub.Runs;

public sealed class RunLogService : IRunLogService
{
    // Output over this many UTF-8 bytes is refused whole rather than cut, as an oversized diff is
    public const int MAX_OUTPUT_BYTES = 262144;

    public const string UNKNOWN_STEP = "UNKNOWN STEP";

    public const string GAP = "--";

    private const string COMPLETED = "completed";

    // A cancelled job or step was stopped rather than failed, and a skipped one never ran
    private static readonly ImmutableHashSet<string> FailedConclusions = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "failure",
        "timed_out",
        "startup_failure",
        "action_required"
    );

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IGitHubRestClient _restClient;

    public RunLogService(IGitHubRestClient restClient, IHttpClientFactory httpClientFactory)
    {
        this._restClient = restClient;
        this._httpClientFactory = httpClientFactory;
    }

    public ValueTask<RunLogResponse> ReadFailedAsync(
        string repository,
        string run,
        string? job,
        int lines,
        string? grep,
        int context,
        int maxMatches,
        int fromLine,
        CancellationToken cancellationToken
    )
    {
        RunLogRequest request = new(
            Repository: GitHubNames.Repository(value: repository, parameterName: nameof(repository)),
            Run: RunQuery.Id(value: run, parameterName: nameof(run)),
            Job: job is null ? null : RunQuery.Id(value: job, parameterName: nameof(job)),
            Lines: RunQuery.Lines(value: lines, parameterName: nameof(lines)),
            Search: grep is null
                ? null
                : new(
                    Pattern: RunQuery.Grep(value: grep, parameterName: nameof(grep)),
                    Context: RunQuery.Context(value: context, parameterName: nameof(context)),
                    MaxMatches: RunQuery.MaxMatches(value: maxMatches, parameterName: nameof(maxMatches)),
                    FromLine: RunQuery.FromLine(value: fromLine, parameterName: nameof(fromLine))
                )
        );

        return this.ReadAsync(request: request, cancellationToken: cancellationToken);
    }

    private async ValueTask<RunLogResponse> ReadAsync(RunLogRequest request, CancellationToken cancellationToken)
    {
        try
        {
            WorkflowRunResponse run = await RunReader.ReadRunAsync(
                restClient: this._restClient,
                repository: request.Repository,
                run: request.Run,
                cancellationToken: cancellationToken
            );

            // Logs exist only once a run has completed, so a run still going is pending before any log is read
            if (request.Job is null && !IsCompleted(run.Status))
            {
                return Outcome(
                    outcome: RunLogOutcome.Pending,
                    message: $"the run {request.Run} of {request.Repository} has not completed"
                );
            }

            IReadOnlyList<WorkflowJobResponse> jobs = await RunReader.ReadJobsAsync(
                restClient: this._restClient,
                repository: request.Repository,
                run: run,
                cancellationToken: cancellationToken
            );

            return await this.ReadJobsAsync(
                request: request,
                run: run,
                jobs: jobs,
                cancellationToken: cancellationToken
            );
        }
        catch (RunNotFoundException exception)
        {
            return Outcome(outcome: RunLogOutcome.NotFound, message: exception.Message);
        }
        catch (RepositoryNotFoundException exception)
        {
            // A missing repository is a failure of the read, not a run that does not exist
            throw new RunLogFailedException(message: exception.Message, innerException: exception);
        }
        catch (Exception exception)
            when (exception is GitHubInvalidResponseException or HttpRequestException or InvalidDataException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new RunLogFailedException(
                message: $"could not read the logs of the run {request.Run} of {request.Repository}",
                innerException: exception
            );
        }
    }

    private async ValueTask<RunLogResponse> ReadJobsAsync(
        RunLogRequest request,
        WorkflowRunResponse run,
        IReadOnlyList<WorkflowJobResponse> jobs,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<WorkflowJobResponse> candidates = jobs;

        if (request.Job is { } jobId)
        {
            candidates = [.. jobs.Where(candidate => candidate.Id == jobId)];
            WorkflowJobResponse? job = candidates.FirstOrDefault();

            if (job is null || job.RunId != request.Run)
            {
                return Outcome(
                    outcome: RunLogOutcome.NotFound,
                    message: $"the job {jobId} is not a job of the run {request.Run} in {request.Repository}"
                );
            }

            if (!IsCompleted(job.Status))
            {
                return Outcome(
                    outcome: RunLogOutcome.Pending,
                    message: $"the job {jobId} of the run {request.Run} of {request.Repository} has not completed"
                );
            }
        }

        WorkflowJobResponse[] failed = [.. candidates.Where(job => IsFailed(job.Conclusion))];

        if (failed.Length == 0)
        {
            return Logged(request: request, log: RunLogText.Empty);
        }

        byte[] content = await this.DownloadAsync(request: request, run: run, cancellationToken: cancellationToken);

        using RunLogArchive archive = new(content);

        RunLogText log = RunLogText.Empty;

        foreach (WorkflowJobResponse job in failed)
        {
            log = log.Add(
                await SectionsAsync(
                    archive: archive,
                    request: request,
                    job: job,
                    kept: log.Kept,
                    cancellationToken: cancellationToken
                )
            );
        }

        return Logged(request: request, log: log);
    }

    private async ValueTask<byte[]> DownloadAsync(
        RunLogRequest request,
        WorkflowRunResponse run,
        CancellationToken cancellationToken
    )
    {
        Uri location;

        try
        {
            location = await this._restClient.GetRedirectLocationAsync(
                relativeUri: $"repos/{request.Repository}/actions/runs/{request.Run}/attempts/{run.RunAttempt}/logs",
                cancellationToken: cancellationToken
            );
        }
        catch (HttpRequestException exception)
            when (exception.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
        {
            string reason = exception.StatusCode == HttpStatusCode.Gone ? "are past their retention" : "were not found";

            throw new RunLogFailedException(
                message: $"the logs of the run {request.Run} of {request.Repository} {reason}",
                innerException: exception
            );
        }

        return await RunLogStorage.DownloadAsync(
            httpClientFactory: this._httpClientFactory,
            location: location,
            cancellationToken: cancellationToken
        );
    }

    // A failed job's failed steps are read from their own files; a job with none, such as one that failed to
    // start, or whose step files are missing, falls back to the job's whole log under the unknown step. kept is how
    // many matches earlier jobs kept, so the most matches a search keeps applies to the whole run
    private static async ValueTask<IReadOnlyList<RunLogSection>> SectionsAsync(
        RunLogArchive archive,
        RunLogRequest request,
        WorkflowJobResponse job,
        int kept,
        CancellationToken cancellationToken
    )
    {
        string jobName = RunLogSanitiser.Name(job.Name);
        List<RunLogSection> steps = [];
        int keptSoFar = kept;

        foreach ((int number, string? name) in FailedSteps(job))
        {
            RunLogLineReader reader = Reader(request: request, kept: keptSoFar);

            if (
                await archive.ReadStepAsync(
                    jobName: job.Name,
                    stepNumber: number,
                    reader: reader,
                    cancellationToken: cancellationToken
                )
            )
            {
                steps.Add(Section(job: jobName, step: RunLogSanitiser.Name(name), reader: reader));
                keptSoFar += reader.Kept;
            }
        }

        if (steps.Count != 0)
        {
            return steps;
        }

        RunLogLineReader whole = Reader(request: request, kept: kept);

        return await archive.ReadJobAsync(jobName: job.Name, reader: whole, cancellationToken: cancellationToken)
            ? [Section(job: jobName, step: UNKNOWN_STEP, reader: whole)]
            : [];
    }

    private static RunLogLineReader Reader(RunLogRequest request, int kept)
    {
        return request.Search is { } search
            ? new RunLogSearchReader(search: search, budget: search.MaxMatches - kept)
            : new RunLogTailReader(request.Lines);
    }

    private static IEnumerable<(int Number, string? Name)> FailedSteps(WorkflowJobResponse job)
    {
        return (job.Steps ?? [])
            .Where(step => step is { Number: not null } && IsFailed(step.Conclusion))
            .Select(step => (Number: step!.Number.GetValueOrDefault(), step.Name))
            .OrderBy(step => step.Number);
    }

    private static RunLogSection Section(string job, string step, RunLogLineReader reader)
    {
        return new(Job: job, Step: step, Lines: reader.Entries, Matches: reader.Matches, Kept: reader.Kept);
    }

    private static RunLogResponse Logged(RunLogRequest request, RunLogText log)
    {
        string text = log.Render();

        if (Encoding.UTF8.GetByteCount(text) > MAX_OUTPUT_BYTES)
        {
            string fewer = request.Search is null ? "fewer lines" : "fewer matches or less context";

            throw new RunLogFailedException(
                $"the failed steps' logs of the run {request.Run} of {request.Repository} are over {MAX_OUTPUT_BYTES} bytes; ask for {fewer}, or one job"
            );
        }

        (int lines, int redacted, int masked) = log.CountLines();
        int? matches = request.Search is null ? null : log.Matches;

        return new(
            Outcome: RunLogOutcome.Log,
            Message: $"read the failed steps' logs of the run {request.Run} of {request.Repository}",
            Log: text,
            Jobs: log.Jobs,
            Steps: log.Sections.Count,
            Lines: lines,
            Redacted: redacted,
            Masked: masked,
            Matches: matches,
            Truncated: matches > log.Kept
        );
    }

    private static RunLogResponse Outcome(RunLogOutcome outcome, string message)
    {
        return new(
            Outcome: outcome,
            Message: message,
            Log: null,
            Jobs: 0,
            Steps: 0,
            Lines: 0,
            Redacted: 0,
            Masked: 0,
            Matches: null,
            Truncated: false
        );
    }

    private static bool IsCompleted(string? status)
    {
        return StringComparer.Ordinal.Equals(x: status, y: COMPLETED);
    }

    private static bool IsFailed(string? conclusion)
    {
        return conclusion is not null && FailedConclusions.Contains(conclusion);
    }

    // Search is null when the tail of each failed step is read
    [DebuggerDisplay("{Repository} {Run} {Job} {Lines}")]
    private sealed record RunLogRequest(string Repository, long Run, long? Job, int Lines, RunLogSearch? Search);

    [DebuggerDisplay("{Job}: {Step}")]
    private sealed record RunLogSection(
        string Job,
        string Step,
        IReadOnlyList<RunLogEntry> Lines,
        int Matches,
        int Kept
    );

    // Jobs counts the jobs that gave at least one section
    [DebuggerDisplay("{Jobs} jobs, {Sections.Count} steps")]
    private sealed record RunLogText(int Jobs, IReadOnlyList<RunLogSection> Sections)
    {
        public static RunLogText Empty { get; } = new(Jobs: 0, Sections: []);

        public int Matches => this.Sections.Sum(section => section.Matches);

        public int Kept => this.Sections.Sum(section => section.Kept);

        public RunLogText Add(IReadOnlyList<RunLogSection> sections)
        {
            return sections.Count == 0 ? this : new(Jobs: this.Jobs + 1, Sections: [.. this.Sections, .. sections]);
        }

        // Counts every line, and the redacted and masked ones, in a single pass
        public (int Lines, int Redacted, int Masked) CountLines()
        {
            int lines = 0;
            int redacted = 0;
            int masked = 0;

            foreach (RunLogLine line in this.Sections.SelectMany(section => section.Lines).Select(entry => entry.Line))
            {
                ++lines;

                if (line.Redacted)
                {
                    ++redacted;
                }

                if (line.Masked)
                {
                    ++masked;
                }
            }

            return (Lines: lines, Redacted: redacted, Masked: masked);
        }

        // Every line ends with a line feed, so the log can be joined to more text without running into it
        public string Render()
        {
            StringBuilder builder = new();

            foreach (RunLogSection section in this.Sections)
            {
                _ = builder.Append("== job ").Append(section.Job).Append(" step ").Append(section.Step).Append('\n');
                int? previous = null;

                foreach (RunLogEntry entry in section.Lines)
                {
                    RenderEntry(builder: builder, entry: entry, previous: previous);
                    previous = entry.Number;
                }
            }

            return builder.ToString();
        }

        // A searched line shows its number then ':' when it matched or '-' when it is context, as grep shows them,
        // and a gap between the searched lines kept is marked as grep marks it
        private static void RenderEntry(StringBuilder builder, in RunLogEntry entry, int? previous)
        {
            if (entry.Number is { } number)
            {
                if (previous is { } last && number != last + 1)
                {
                    _ = builder.Append(GAP).Append('\n');
                }

                _ = builder
                    .Append("| ")
                    .Append(number.ToString(CultureInfo.InvariantCulture))
                    .Append(entry.Match ? ':' : '-');
            }
            else
            {
                _ = builder.Append('|');
            }

            string text = entry.Line.Text;

            _ = text.Length == 0 ? builder.Append('\n') : builder.Append(' ').Append(text).Append('\n');
        }
    }
}
