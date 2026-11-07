using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Runs;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using NSubstitute;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Runs;

public sealed class RunLogServiceTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string FAILED = "could not read the logs of the run 5 of credfeto/credfeto-workflow-mcp";
    private const string READ = "read the failed steps' logs of the run 5 of credfeto/credfeto-workflow-mcp";
    private const string API = GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY;
    private const string STORAGE = "https://results-receiver.actions.githubusercontent.com/rest/logs.zip?sig=abc";

    private const int MEGABYTE = 1024 * 1024;

    // One megabyte past the 256 MiB the archive may decompress to
    private const int OVER_DECOMPRESSED_CAP_MEGABYTES = 257;

    private static readonly string FailedSteps =
        RunJson.Step(number: 1, name: "Set up job", conclusion: "success")
        + ","
        + RunJson.Step(number: 3, name: "Compile", conclusion: "failure");

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private static HttpResponseMessage Redirect(string location)
    {
        return new(HttpStatusCode.Found) { Headers = { Location = new Uri(location) } };
    }

    private static HttpResponseMessage Content(byte[] content)
    {
        return new(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };
    }

    private async Task<byte[]> ZipAsync(params (string Name, string Text)[] entries)
    {
        await using MemoryStream stream = new();

        await using (ZipArchive archive = new(stream: stream, mode: ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string text) in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(name);

                await using Stream entryStream = await entry.OpenAsync(this.CancellationToken());
                await entryStream.WriteAsync(
                    buffer: Encoding.UTF8.GetBytes(text),
                    cancellationToken: this.CancellationToken()
                );
            }
        }

        return stream.ToArray();
    }

    // One entry of the same byte over and over, which deflate shrinks to a tiny fraction of its size
    private async Task<byte[]> RepeatedZipAsync(string name, int megabytes)
    {
        byte[] chunk = new byte[MEGABYTE];
        Array.Fill(array: chunk, value: (byte)'a');

        await using MemoryStream stream = new();

        await using (ZipArchive archive = new(stream: stream, mode: ZipArchiveMode.Create, leaveOpen: true))
        {
            ZipArchiveEntry entry = archive.CreateEntry(entryName: name, compressionLevel: CompressionLevel.Optimal);

            await using Stream entryStream = await entry.OpenAsync(this.CancellationToken());

            for (int written = 0; written < megabytes; ++written)
            {
                await entryStream.WriteAsync(buffer: chunk, cancellationToken: this.CancellationToken());
            }
        }

        return stream.ToArray();
    }

    // The run, the jobs of its latest attempt, then the redirect to the log storage
    private static Func<int, HttpResponseMessage> Upstream(string jobs, string location = STORAGE)
    {
        return index =>
            index switch
            {
                0 => Responses.Json(RunJson.Run(5)),
                1 => Responses.Json(jobs),
                2 => Redirect(location),
                _ => Unexpected(index),
            };
    }

    private static string FailedBuild(string name = "build")
    {
        return RunJson.Jobs(1, RunJson.Job(id: 20, runId: 5, name: name, steps: FailedSteps));
    }

    private async Task<RunLogResponse> ReadAsync(
        RecordingHttpMessageHandler upstream,
        RecordingHttpMessageHandler? storage = null,
        string? job = null,
        int lines = 100,
        string run = "5"
    )
    {
        IHttpClientFactory factory = MockHttpClientFactoryCreateClient(upstream);

        if (storage is not null)
        {
            factory
                .CreateClient(GitHubSetup.LogStorageHttpClientName)
                .Returns(_ => new HttpClient(handler: storage, disposeHandler: false));
        }

        GitHubRestClient rest = new(httpClientFactory: factory, logger: this.GetTypedLogger<GitHubRestClient>());

        return await new RunLogService(restClient: rest, httpClientFactory: factory).ReadFailedAsync(
            repository: REPOSITORY,
            run: run,
            job: job,
            lines: lines,
            cancellationToken: this.CancellationToken()
        );
    }

    private async Task<RunLogResponse> ReadZipAsync(string jobs, byte[] zip, string? job = null, int lines = 100)
    {
        using RecordingHttpMessageHandler upstream = new(Upstream(jobs));
        using RecordingHttpMessageHandler storage = new(_ => Content(zip));

        return await this.ReadAsync(upstream: upstream, storage: storage, job: job, lines: lines);
    }

    private static RunLogResponse Outcome(RunLogOutcome outcome, string message)
    {
        return new(Outcome: outcome, Message: message, Log: null, Jobs: 0, Steps: 0, Lines: 0, Redacted: 0, Masked: 0);
    }

    [Fact]
    public async Task FailedStepIsReadFromItsFileAsync()
    {
        byte[] zip = await this.ZipAsync(
            ("build/1_Set up job.txt", "setting up\n"),
            ("build/3_Compile.txt", "a\n\nb\n")
        );
        using RecordingHttpMessageHandler upstream = new(Upstream(FailedBuild()));
        using RecordingHttpMessageHandler storage = new(_ => Content(zip));

        RunLogResponse response = await this.ReadAsync(upstream: upstream, storage: storage);

        Assert.Equal(
            expected: new RunLogResponse(
                Outcome: RunLogOutcome.Log,
                Message: READ,
                Log: "== job build step Compile\n| a\n|\n| b\n",
                Jobs: 1,
                Steps: 1,
                Lines: 3,
                Redacted: 0,
                Masked: 0
            ),
            actual: response
        );
        Assert.Equal(
            expected:
            [
                new Uri(API + "/actions/runs/5"),
                new Uri(API + "/actions/runs/5/attempts/2/jobs?per_page=100"),
                new Uri(API + "/actions/runs/5/attempts/2/logs"),
            ],
            actual: [.. upstream.Requests.Select(request => request.RequestUri)]
        );

        RecordedRequest download = Assert.Single(storage.Requests);
        Assert.Equal(expected: new Uri(STORAGE), actual: download.RequestUri);
        Assert.Null(download.Authorization);
    }

    [Fact]
    public async Task EveryFailedStepOfEveryFailedJobIsReadInOrderAsync()
    {
        string jobs = RunJson.Jobs(
            3,
            RunJson.Job(
                id: 30,
                runId: 5,
                name: "test",
                steps: RunJson.Step(number: 4, name: "Test", conclusion: "timed_out")
                    + ","
                    + RunJson.Step(number: 2, name: "Restore", conclusion: "failure")
            ),
            RunJson.Job(id: 20, runId: 5, name: "build", steps: FailedSteps),
            RunJson.Job(id: 40, runId: 5, name: "lint", conclusion: "success")
        );

        RunLogResponse response = await this.ReadZipAsync(
            jobs: jobs,
            zip: await this.ZipAsync(
                ("build/3_Compile.txt", "c\n"),
                ("test/2_Restore.txt", "r\n"),
                ("test/4_Test.txt", "t\n"),
                ("lint/1_Lint.txt", "l\n")
            )
        );

        Assert.Equal(
            expected: "== job build step Compile\n| c\n== job test step Restore\n| r\n== job test step Test\n| t\n",
            actual: response.Log
        );
        Assert.Equal(expected: 2, actual: response.Jobs);
        Assert.Equal(expected: 3, actual: response.Steps);
        Assert.Equal(expected: 3, actual: response.Lines);
    }

    [Fact]
    public async Task OnlyTheChosenJobIsReadAsync()
    {
        string jobs = RunJson.Jobs(
            2,
            RunJson.Job(id: 20, runId: 5, name: "build", steps: FailedSteps),
            RunJson.Job(id: 30, runId: 5, name: "test", steps: FailedSteps)
        );

        RunLogResponse response = await this.ReadZipAsync(
            jobs: jobs,
            zip: await this.ZipAsync(("build/3_Compile.txt", "b\n"), ("test/3_Compile.txt", "t\n")),
            job: "30"
        );

        Assert.Equal(expected: "== job test step Compile\n| t\n", actual: response.Log);
    }

    [Fact]
    public async Task OnlyTheLastLinesAreKeptAsync()
    {
        RunLogResponse response = await this.ReadZipAsync(
            jobs: FailedBuild(),
            zip: await this.ZipAsync(("build/3_Compile.txt", "1\n2\n3\n4")),
            lines: 2
        );

        Assert.Equal(expected: "== job build step Compile\n| 3\n| 4\n", actual: response.Log);
    }

    [Fact]
    public async Task ByteOrderMarkAndCarriageReturnsAreDroppedAsync()
    {
        RunLogResponse response = await this.ReadZipAsync(
            jobs: FailedBuild(),
            zip: await this.ZipAsync(("build/3_Compile.txt", "﻿a\r\nb\r\n"))
        );

        Assert.Equal(expected: "== job build step Compile\n| a\n| b\n", actual: response.Log);
    }

    [Fact]
    public async Task JobFolderDropsSlashesAndColonsAsync()
    {
        RunLogResponse response = await this.ReadZipAsync(
            jobs: FailedBuild("build / linux: x64"),
            zip: await this.ZipAsync(("build  linux x64/3_Compile.txt", "a\n"))
        );

        Assert.Equal(expected: "== job build / linux: x64 step Compile\n| a\n", actual: response.Log);
    }

    [Theory]
    [InlineData("0_build.txt")]
    [InlineData("-1_build.txt")]
    public async Task JobWithNoFailedStepFallsBackToItsWholeLogAsync(string file)
    {
        string jobs = RunJson.Jobs(
            1,
            RunJson.Job(
                id: 20,
                runId: 5,
                conclusion: "startup_failure",
                steps: RunJson.Step(number: 1, name: "Set up job", conclusion: "success")
            )
        );

        RunLogResponse response = await this.ReadZipAsync(jobs: jobs, zip: await this.ZipAsync((file, "boom\n")));

        Assert.Equal(expected: "== job build step " + RunLogService.UNKNOWN_STEP + "\n| boom\n", actual: response.Log);
        Assert.Equal(expected: 1, actual: response.Jobs);
    }

    [Fact]
    public async Task FailedStepWithNoFileFallsBackToTheWholeLogAsync()
    {
        RunLogResponse response = await this.ReadZipAsync(
            jobs: FailedBuild(),
            zip: await this.ZipAsync(("build/1_Set up job.txt", "setting up\n"), ("2_build.txt", "whole\n"))
        );

        Assert.Equal(expected: "== job build step " + RunLogService.UNKNOWN_STEP + "\n| whole\n", actual: response.Log);
    }

    [Fact]
    public async Task JobWithNoFilesGivesNoSectionAsync()
    {
        RunLogResponse response = await this.ReadZipAsync(
            jobs: FailedBuild(),
            zip: await this.ZipAsync(("other/3_Compile.txt", "a\n"), ("1_other.txt", "b\n"))
        );

        Assert.Equal(
            expected: new RunLogResponse(
                Outcome: RunLogOutcome.Log,
                Message: READ,
                Log: "",
                Jobs: 0,
                Steps: 0,
                Lines: 0,
                Redacted: 0,
                Masked: 0
            ),
            actual: response
        );
    }

    [Fact]
    public async Task SecretsAreRedactedAndMaskedAndCountedAsync()
    {
        string hex = string.Concat(Enumerable.Repeat(element: "0123456789", count: 5));

        RunLogResponse response = await this.ReadZipAsync(
            jobs: FailedBuild(),
            zip: await this.ZipAsync(("build/3_Compile.txt", "token gh" + "p_abc\nvalue " + hex + "\nplain\n"))
        );

        Assert.Equal(
            expected: "== job build step Compile\n| "
                + RunLogSanitiser.REDACTED_LINE
                + "\n| value "
                + RunLogSanitiser.MASK
                + "\n| plain\n",
            actual: response.Log
        );
        Assert.Equal(expected: 1, actual: response.Redacted);
        Assert.Equal(expected: 1, actual: response.Masked);
        Assert.Equal(expected: 3, actual: response.Lines);
    }

    [Fact]
    public async Task ArchiveDecompressingPastItsCapIsAFailureAsync()
    {
        byte[] zip = await this.RepeatedZipAsync(
            name: "build/3_Compile.txt",
            megabytes: OVER_DECOMPRESSED_CAP_MEGABYTES
        );

        RunLogFailedException exception = await Assert.ThrowsAsync<RunLogFailedException>(() =>
            this.ReadZipAsync(jobs: FailedBuild(), zip: zip)
        );

        Assert.Equal(expected: "the run's logs are too large to read", actual: exception.Message);
    }

    [Fact]
    public async Task LogOverTheCapIsAFailureAsync()
    {
        string line = new('x', RunLogSanitiser.MAX_LINE_BYTES);
        string text = string.Concat(Enumerable.Repeat(element: line + "\n", count: 300));
        byte[] zip = await this.ZipAsync(("build/3_Compile.txt", text));

        RunLogFailedException exception = await Assert.ThrowsAsync<RunLogFailedException>(() =>
            this.ReadZipAsync(jobs: FailedBuild(), zip: zip, lines: 2000)
        );

        Assert.Equal(
            expected: "the failed steps' logs of the run 5 of credfeto/credfeto-workflow-mcp are over 262144 bytes; ask for fewer lines, or one job",
            actual: exception.Message
        );
    }

    [Fact]
    public async Task RunWithNoFailedJobReadsNoLogAsync()
    {
        using RecordingHttpMessageHandler upstream = new(
            Upstream(
                RunJson.Jobs(
                    2,
                    RunJson.Job(id: 20, runId: 5, conclusion: "success"),
                    RunJson.Job(id: 21, runId: 5, conclusion: "cancelled")
                )
            )
        );

        RunLogResponse response = await this.ReadAsync(upstream);

        Assert.Equal(
            expected: new RunLogResponse(
                Outcome: RunLogOutcome.Log,
                Message: READ,
                Log: "",
                Jobs: 0,
                Steps: 0,
                Lines: 0,
                Redacted: 0,
                Masked: 0
            ),
            actual: response
        );
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task RunNotCompletedIsPendingAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0 ? Responses.Json(RunJson.Run(id: 5, status: "in_progress", conclusion: "")) : Unexpected(index)
        );

        RunLogResponse response = await this.ReadAsync(upstream);

        Assert.Equal(
            expected: Outcome(
                outcome: RunLogOutcome.Pending,
                message: "the run 5 of credfeto/credfeto-workflow-mcp has not completed"
            ),
            actual: response
        );
    }

    [Fact]
    public async Task CompletedJobOfARunStillGoingIsReadAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Responses.Json(RunJson.Run(id: 5, status: "in_progress", conclusion: "")),
                1 => Responses.Json(FailedBuild()),
                2 => Redirect(STORAGE),
                _ => Unexpected(index),
            }
        );
        byte[] zip = await this.ZipAsync(("build/3_Compile.txt", "a\n"));
        using RecordingHttpMessageHandler storage = new(_ => Content(zip));

        RunLogResponse response = await this.ReadAsync(upstream: upstream, storage: storage, job: "20");

        Assert.Equal(expected: "== job build step Compile\n| a\n", actual: response.Log);
    }

    [Fact]
    public async Task JobNotCompletedIsPendingAsync()
    {
        using RecordingHttpMessageHandler upstream = new(
            Upstream(RunJson.Jobs(1, RunJson.Job(id: 20, runId: 5, status: "in_progress", conclusion: "")))
        );

        RunLogResponse response = await this.ReadAsync(upstream: upstream, job: "20");

        Assert.Equal(
            expected: Outcome(
                outcome: RunLogOutcome.Pending,
                message: "the job 20 of the run 5 of credfeto/credfeto-workflow-mcp has not completed"
            ),
            actual: response
        );
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Theory]
    [InlineData("99")]
    [InlineData("21")]
    public async Task JobNotOfTheRunIsNotFoundAsync(string job)
    {
        using RecordingHttpMessageHandler upstream = new(
            Upstream(RunJson.Jobs(2, RunJson.Job(id: 20, runId: 5), RunJson.Job(id: 21, runId: 6)))
        );

        RunLogResponse response = await this.ReadAsync(upstream: upstream, job: job);

        Assert.Equal(
            expected: Outcome(
                outcome: RunLogOutcome.NotFound,
                message: "the job " + job + " is not a job of the run 5 in credfeto/credfeto-workflow-mcp"
            ),
            actual: response
        );
    }

    [Fact]
    public async Task MissingRunIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0
                ? Responses.Status(HttpStatusCode.NotFound)
                : Responses.Json("{\"full_name\":\"credfeto/credfeto-workflow-mcp\",\"default_branch\":\"main\"}")
        );

        RunLogResponse response = await this.ReadAsync(upstream);

        Assert.Equal(
            expected: Outcome(
                outcome: RunLogOutcome.NotFound,
                message: "the run 5 was not found in credfeto/credfeto-workflow-mcp"
            ),
            actual: response
        );
    }

    [Fact]
    public async Task MissingRepositoryIsAFailureAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Status(HttpStatusCode.NotFound));

        RunLogFailedException exception = await Assert.ThrowsAsync<RunLogFailedException>(() =>
            this.ReadAsync(upstream)
        );

        Assert.Equal(
            expected: "the repository credfeto/credfeto-workflow-mcp was not found, or the caller cannot see it",
            actual: exception.Message
        );
    }

    [Fact]
    public async Task UpstreamFailureIsAFailureAsync()
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Status(HttpStatusCode.BadGateway));

        RunLogFailedException exception = await Assert.ThrowsAsync<RunLogFailedException>(() =>
            this.ReadAsync(upstream)
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
    }

    [Theory]
    [InlineData(
        HttpStatusCode.Gone,
        "the logs of the run 5 of credfeto/credfeto-workflow-mcp are past their retention"
    )]
    [InlineData(HttpStatusCode.NotFound, "the logs of the run 5 of credfeto/credfeto-workflow-mcp were not found")]
    public async Task LogsThatCannotBeReachedAreAFailureAsync(HttpStatusCode status, string message)
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Responses.Json(RunJson.Run(5)),
                1 => Responses.Json(FailedBuild()),
                2 => Responses.Status(status),
                _ => Unexpected(index),
            }
        );

        RunLogFailedException exception = await Assert.ThrowsAsync<RunLogFailedException>(() =>
            this.ReadAsync(upstream)
        );

        Assert.Equal(expected: message, actual: exception.Message);
    }

    [Fact]
    public async Task LogLocationOffTheStorageHostsIsNotReadAsync()
    {
        using RecordingHttpMessageHandler upstream = new(
            Upstream(jobs: FailedBuild(), location: "https://example.com/logs.zip?sig=abc")
        );
        using RecordingHttpMessageHandler storage = new(Unexpected);

        RunLogFailedException exception = await Assert.ThrowsAsync<RunLogFailedException>(() =>
            this.ReadAsync(upstream: upstream, storage: storage)
        );

        Assert.Equal(
            expected: "GitHub returned a log location on an unexpected host example.com",
            actual: exception.Message
        );
        Assert.Empty(storage.Requests);
    }

    [Fact]
    public async Task StorageFailureIsAFailureAsync()
    {
        using RecordingHttpMessageHandler upstream = new(Upstream(FailedBuild()));
        using RecordingHttpMessageHandler storage = new(_ => Responses.Status(HttpStatusCode.Forbidden));

        RunLogFailedException exception = await Assert.ThrowsAsync<RunLogFailedException>(() =>
            this.ReadAsync(upstream: upstream, storage: storage)
        );

        Assert.Equal(expected: "the log storage answered 403 for the run's logs", actual: exception.Message);
    }

    [Fact]
    public async Task ContentThatIsNotAnArchiveIsAFailureAsync()
    {
        RunLogFailedException exception = await Assert.ThrowsAsync<RunLogFailedException>(() =>
            this.ReadZipAsync(jobs: FailedBuild(), zip: Encoding.UTF8.GetBytes("not an archive"))
        );

        Assert.Equal(expected: FAILED, actual: exception.Message);
    }

    [Theory]
    [InlineData("0", null, 100, "run")]
    [InlineData("5", "x", 100, "job")]
    [InlineData("5", null, 0, "lines")]
    [InlineData("5", null, 2001, "lines")]
    public async Task InvalidRequestIsRejectedWithoutRequestAsync(string run, string? job, int lines, string parameter)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.ReadAsync(upstream: upstream, job: job, lines: lines, run: run)
        );

        Assert.Equal(expected: parameter, actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }
}
