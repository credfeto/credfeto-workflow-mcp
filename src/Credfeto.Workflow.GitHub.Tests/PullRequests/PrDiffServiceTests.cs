using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.PullRequests;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.PullRequests;

public sealed class PrDiffServiceTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const int PULL_REQUEST = 50;
    private const string TARGET = REPOSITORY + "#50";
    private const string DIFF_MEDIA_TYPE = "application/vnd.github.diff";
    private const char LINE_SEPARATOR = (char)0x2028;
    private const char PARAGRAPH_SEPARATOR = (char)0x2029;

    private const string CLEAN_DIFF = """
        diff --git a/README.md b/README.md
        index 0000000..1111111 100644
        --- a/README.md
        +++ b/README.md
        @@ -1 +1 @@
        -old
        +new

        """;

    private static readonly Uri PullRequestUri = new(GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/pulls/50");

    private static readonly Uri FilesUri = new(
        GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/pulls/50/files?per_page=100"
    );

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private static HttpResponseMessage Throw(string message)
    {
        throw new HttpRequestException(message);
    }

    private static RecordingHttpMessageHandler Single(Func<HttpResponseMessage> respond)
    {
        return new(index => index == 0 ? respond() : Unexpected(index));
    }

    private static RecordingHttpMessageHandler Text(string body)
    {
        return Single(() => Responses.WithContentType(body: body, contentType: "text/plain"));
    }

    private static RecordingHttpMessageHandler Files(string json)
    {
        return Single(() => Responses.Json(json));
    }

    private async Task<PrDiffResult> DiffAsync(
        RecordingHttpMessageHandler upstream,
        string? mode,
        string repository = REPOSITORY,
        int pullRequest = PULL_REQUEST
    )
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        return await new PrDiffService(restClient: rest).DiffAsync(
            repository: repository,
            pullRequest: pullRequest,
            mode: mode,
            cancellationToken: this.CancellationToken()
        );
    }

    private static void AssertFailed(PrDiffResult result, PrDiffOutcome outcome, PrDiffMode? mode, string message)
    {
        Assert.Equal(expected: outcome, actual: result.Outcome);
        Assert.Equal(expected: mode, actual: result.Mode);
        Assert.Contains(
            expectedSubstring: message,
            actualString: result.Message,
            comparisonType: StringComparison.Ordinal
        );
        Assert.Null(result.Diff);
        Assert.Null(result.Files);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("diff")]
    public async Task DiffModeReadsTheUnifiedDiffAsync(string? mode)
    {
        using RecordingHttpMessageHandler upstream = Text(CLEAN_DIFF);

        PrDiffResult result = await this.DiffAsync(upstream: upstream, mode: mode);

        Assert.Equal(expected: PrDiffOutcome.Success, actual: result.Outcome);
        Assert.Equal(expected: "read the diff of " + TARGET, actual: result.Message);
        Assert.Equal(expected: PrDiffMode.Diff, actual: result.Mode);
        Assert.Equal(expected: CLEAN_DIFF, actual: result.Diff);
        Assert.Null(result.Files);
        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: HttpMethod.Get, actual: request.Method);
        Assert.Equal(expected: PullRequestUri, actual: request.RequestUri);
        Assert.Equal(expected: DIFF_MEDIA_TYPE, actual: request.Accept);
    }

    [Fact]
    public async Task DiffModeReplacesEscapeAndHiddenLineCharactersAsync()
    {
        using RecordingHttpMessageHandler upstream = Text(
            "+a\u001b[31mred\r\n+b" + LINE_SEPARATOR + "c" + PARAGRAPH_SEPARATOR + "d\u0085e\u007f\tf\n"
        );

        PrDiffResult result = await this.DiffAsync(upstream: upstream, mode: "diff");

        Assert.Equal(expected: PrDiffOutcome.Success, actual: result.Outcome);
        Assert.Equal(expected: "+a [31mred \n+b c d e \tf\n", actual: result.Diff);
    }

    [Fact]
    public async Task NameOnlyModeGivesEachFileStatusLetterAndPathAsync()
    {
        using RecordingHttpMessageHandler upstream = Files(
            """
            [
              {"filename":"added.cs","status":"added","additions":1,"deletions":0},
              {"filename":"modified.cs","status":"modified","additions":1,"deletions":1},
              {"filename":"removed.cs","status":"removed","additions":0,"deletions":1},
              {"filename":"new-name.cs","status":"renamed","additions":0,"deletions":0},
              {"filename":"copied.cs","status":"copied","additions":0,"deletions":0},
              {"filename":"changed.cs","status":"changed","additions":0,"deletions":0},
              {"filename":"unchanged.cs","status":"unchanged","additions":0,"deletions":0}
            ]
            """
        );

        PrDiffResult result = await this.DiffAsync(upstream: upstream, mode: "name-only");

        Assert.Equal(expected: PrDiffOutcome.Success, actual: result.Outcome);
        Assert.Equal(expected: "read the changed files of " + TARGET, actual: result.Message);
        Assert.Equal(expected: PrDiffMode.NameOnly, actual: result.Mode);
        Assert.Null(result.Diff);
        Assert.Equal(
            expected:
            [
                new PrDiffFile(Status: "A", Path: "added.cs", Additions: null, Deletions: null),
                new PrDiffFile(Status: "M", Path: "modified.cs", Additions: null, Deletions: null),
                new PrDiffFile(Status: "D", Path: "removed.cs", Additions: null, Deletions: null),
                new PrDiffFile(Status: "R", Path: "new-name.cs", Additions: null, Deletions: null),
                new PrDiffFile(Status: "C", Path: "copied.cs", Additions: null, Deletions: null),
                new PrDiffFile(Status: "-", Path: "changed.cs", Additions: null, Deletions: null),
                new PrDiffFile(Status: "-", Path: "unchanged.cs", Additions: null, Deletions: null),
            ],
            actual: result.Files
        );
        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: HttpMethod.Get, actual: request.Method);
        Assert.Equal(expected: FilesUri, actual: request.RequestUri);
    }

    [Fact]
    public async Task StatModeGivesEachFileLineCountsAsync()
    {
        using RecordingHttpMessageHandler upstream = Files(
            """
            [
              {"filename":"src/a.cs","status":"modified","additions":12,"deletions":3},
              {"filename":"image.png","status":"added","additions":0,"deletions":0},
              {"filename":"new-name.cs","status":"renamed","additions":1,"deletions":2}
            ]
            """
        );

        PrDiffResult result = await this.DiffAsync(upstream: upstream, mode: "stat");

        Assert.Equal(expected: PrDiffOutcome.Success, actual: result.Outcome);
        Assert.Equal(expected: "read the changed files of " + TARGET, actual: result.Message);
        Assert.Equal(expected: PrDiffMode.Stat, actual: result.Mode);
        Assert.Null(result.Diff);
        Assert.Equal(
            expected:
            [
                new PrDiffFile(Status: null, Path: "src/a.cs", Additions: 12, Deletions: 3),
                new PrDiffFile(Status: null, Path: "image.png", Additions: 0, Deletions: 0),
                new PrDiffFile(Status: null, Path: "new-name.cs", Additions: 1, Deletions: 2),
            ],
            actual: result.Files
        );
        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: FilesUri, actual: request.RequestUri);
    }

    [Fact]
    public async Task FilePathLineBreakingCharactersAreReplacedAsync()
    {
        using RecordingHttpMessageHandler upstream = Files(
            """[{"filename":"a\nb\u001b[0m"""
                + LINE_SEPARATOR
                + """c.cs","status":"added","additions":1,"deletions":0}]"""
        );

        PrDiffResult result = await this.DiffAsync(upstream: upstream, mode: "name-only");

        PrDiffFile file = Assert.Single(result.Files!);
        Assert.Equal(expected: "a b [0m c.cs", actual: file.Path);
    }

    [Fact]
    public async Task FilesAreReadAcrossEveryPageAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Responses.Json(
                    json: """[{"filename":"first.cs","status":"added","additions":1,"deletions":0}]""",
                    nextLink: "<" + FilesUri.AbsoluteUri + "&page=2>; rel=\"next\""
                ),
                1 => Responses.Json("""[{"filename":"second.cs","status":"removed","additions":0,"deletions":4}]"""),
                _ => Unexpected(index),
            }
        );

        PrDiffResult result = await this.DiffAsync(upstream: upstream, mode: "stat");

        Assert.Equal(
            expected:
            [
                new PrDiffFile(Status: null, Path: "first.cs", Additions: 1, Deletions: 0),
                new PrDiffFile(Status: null, Path: "second.cs", Additions: 0, Deletions: 4),
            ],
            actual: result.Files
        );
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Theory]
    [InlineData("""[{"filename":"a.cs","status":"bogus","additions":1,"deletions":0}]""")]
    [InlineData("""[{"filename":"a.cs","additions":1,"deletions":0}]""")]
    [InlineData("""[{"status":"added","additions":1,"deletions":0}]""")]
    [InlineData("""[{"filename":"","status":"added","additions":1,"deletions":0}]""")]
    [InlineData("""[null]""")]
    public async Task NameOnlyModeRejectsAnUnreadableFileAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = Files(json);

        PrDiffResult result = await this.DiffAsync(upstream: upstream, mode: "name-only");

        AssertFailed(
            result: result,
            outcome: PrDiffOutcome.RuntimeError,
            mode: PrDiffMode.NameOnly,
            message: "could not read the diff of " + TARGET
        );
    }

    [Theory]
    [InlineData("""[{"filename":"a.cs","status":"added","deletions":0}]""")]
    [InlineData("""[{"filename":"a.cs","status":"added","additions":1}]""")]
    [InlineData("""[{"filename":"a.cs","status":"added","additions":-1,"deletions":0}]""")]
    [InlineData("""[{"filename":"a.cs","status":"added","additions":1,"deletions":-1}]""")]
    [InlineData("""[{"status":"added","additions":1,"deletions":0}]""")]
    [InlineData("""[null]""")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("""{"filename":"a.cs"}""")]
    public async Task StatModeRejectsAnUnreadableFileAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = Files(json);

        PrDiffResult result = await this.DiffAsync(upstream: upstream, mode: "stat");

        AssertFailed(
            result: result,
            outcome: PrDiffOutcome.RuntimeError,
            mode: PrDiffMode.Stat,
            message: "could not read the diff of " + TARGET
        );
    }

    [Theory]
    [InlineData("Diff")]
    [InlineData("name_only")]
    [InlineData("stats")]
    [InlineData(" ")]
    public async Task UnknownModeIsAUsageErrorWithNoRequestAsync(string mode)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        PrDiffResult result = await this.DiffAsync(upstream: upstream, mode: mode);

        AssertFailed(
            result: result,
            outcome: PrDiffOutcome.UsageError,
            mode: null,
            message: "Parameter 'mode' is not valid: it must be diff, name-only or stat"
        );
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-slash")]
    [InlineData("owner/repo/extra")]
    public async Task InvalidRepositoryIsAUsageErrorWithNoRequestAsync(string repository)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        PrDiffResult result = await this.DiffAsync(upstream: upstream, mode: null, repository: repository);

        AssertFailed(
            result: result,
            outcome: PrDiffOutcome.UsageError,
            mode: null,
            message: "Parameter 'repository' is not valid"
        );
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task InvalidPullRequestIsAUsageErrorWithNoRequestAsync(int pullRequest)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        PrDiffResult result = await this.DiffAsync(upstream: upstream, mode: null, pullRequest: pullRequest);

        AssertFailed(
            result: result,
            outcome: PrDiffOutcome.UsageError,
            mode: null,
            message: "Parameter 'pullRequest' is not valid: must be above 0"
        );
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData(null, PrDiffMode.Diff)]
    [InlineData("name-only", PrDiffMode.NameOnly)]
    [InlineData("stat", PrDiffMode.Stat)]
    public async Task MissingPullRequestIsNotFoundAsync(string? mode, PrDiffMode expectedMode)
    {
        using RecordingHttpMessageHandler upstream = Single(static () => Responses.Status(HttpStatusCode.NotFound));

        PrDiffResult result = await this.DiffAsync(upstream: upstream, mode: mode);

        AssertFailed(
            result: result,
            outcome: PrDiffOutcome.NotFound,
            mode: expectedMode,
            message: "the pull request " + TARGET + " was not found, or cannot be seen"
        );
    }

    [Theory]
    [InlineData(null, PrDiffMode.Diff)]
    [InlineData("stat", PrDiffMode.Stat)]
    public async Task UpstreamFailureIsARuntimeErrorAsync(string? mode, PrDiffMode expectedMode)
    {
        using RecordingHttpMessageHandler upstream = Single(static () =>
            Responses.Status(HttpStatusCode.InternalServerError)
        );

        PrDiffResult result = await this.DiffAsync(upstream: upstream, mode: mode);

        AssertFailed(
            result: result,
            outcome: PrDiffOutcome.RuntimeError,
            mode: expectedMode,
            message: "could not read the diff of " + TARGET
        );
    }

    [Fact]
    public async Task UnreachableUpstreamIsARuntimeErrorAsync()
    {
        using RecordingHttpMessageHandler upstream = Single(static () => Throw("connection refused"));

        PrDiffResult result = await this.DiffAsync(upstream: upstream, mode: null);

        AssertFailed(
            result: result,
            outcome: PrDiffOutcome.RuntimeError,
            mode: PrDiffMode.Diff,
            message: "could not read the diff of " + TARGET
        );
        Assert.DoesNotContain(
            expectedSubstring: "connection refused",
            actualString: result.Message,
            comparisonType: StringComparison.Ordinal
        );
    }
}
