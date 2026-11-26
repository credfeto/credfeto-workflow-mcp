using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.PullRequests;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.PullRequests;

public sealed class PrTakeBackServiceTests : GitHubClientTestBase
{
    private const string OWNER = "credfeto";
    private const string NAME = "credfeto-workflow-mcp";
    private const string REPOSITORY = OWNER + "/" + NAME;
    private const int PR = 93;
    private const string BODY = "Taking this back to fix the review findings.";
    private const string NODE_ID = "PR_kwDO1";
    private const string FIELD_ID = "PVTSSF_1";
    private const string FIELD_NAME = "Status";
    private const string OPTION_ID = "OPT_DEV";
    private const string COMMENT_URL = "https://github.com/" + REPOSITORY + "/pull/93#issuecomment-555";

    private const string AUTO_MERGE_ON_FIELD = "{\"enabled_by\":{\"login\":\"credfeto\"},\"merge_method\":\"merge\"}";

    private const string AUTO_MERGE_OFF =
        """{"data":{"disablePullRequestAutoMerge":{"pullRequest":{"autoMergeRequest":null}}}}""";

    private const string DRAFT = """{"data":{"convertPullRequestToDraft":{"pullRequest":{"isDraft":true}}}}""";

    private const string GRAPHQL_ERROR = """{"errors":[{"message":"Not found"}],"data":null}""";

    private const string STEP_AUTO_MERGE = "auto-merge off";
    private const string STEP_DRAFT = "draft";
    private const string STEP_BOARD = "Workflow board set to Development";
    private const string STEP_COMMENT = "comment posted";

    private static readonly WorkflowProject Project = new(ProjectNodeId: "PVT_1", Number: 7, Owner: OWNER);

    private static readonly Uri PullUri = new(GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/pulls/93");

    private static readonly Uri CommentsUri = new(
        GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/issues/93/comments"
    );

    private static readonly Uri GraphQlUri = new(GitHubServices.ApiBaseUrl + "graphql");

    private readonly IWorkflowBoardResolver _boardResolver;
    private readonly IBoardWriter _boardWriter;
    private readonly ILeakChecker _leakChecker;

    public PrTakeBackServiceTests()
    {
        this._leakChecker = GetSubstitute<ILeakChecker>();
        this._boardWriter = GetSubstitute<IBoardWriter>();
        this._boardResolver = GetSubstitute<IWorkflowBoardResolver>();

        this._boardResolver.ResolveAsync(repository: Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Project);

        this._leakChecker.CheckRepositoryReferencesAsync(
                repository: Arg.Any<string>(),
                subject: Arg.Any<LeakSubject>(),
                text: Arg.Any<string>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns((LeakRefusal?)null);
    }

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private static RecordingHttpMessageHandler Flow(params Func<HttpResponseMessage>[] responses)
    {
        return new(index => index < responses.Length ? responses[index]() : Unexpected(index));
    }

    private static HttpResponseMessage PullRequest(bool draft = false, bool autoMerge = true, string state = "open")
    {
        string draftText = draft ? "true" : "false";
        string autoMergeText = autoMerge ? AUTO_MERGE_ON_FIELD : "null";

        return Responses.Json(
            "{\"node_id\":\""
                + NODE_ID
                + "\",\"state\":\""
                + state
                + "\",\"draft\":"
                + draftText
                + ",\"auto_merge\":"
                + autoMergeText
                + "}"
        );
    }

    private static HttpResponseMessage Posted(string url = COMMENT_URL)
    {
        return Responses.StatusWithJson(
            statusCode: HttpStatusCode.Created,
            json: "{\"id\":555,\"html_url\":\"" + url + "\"}"
        );
    }

    private static HttpResponseMessage Json(string json)
    {
        return Responses.Json(json);
    }

    private static HttpResponseMessage TransportFails()
    {
        throw new HttpRequestException("connection reset");
    }

    // A ready pull request with auto-merge on: every write is made
    private static RecordingHttpMessageHandler FullFlow()
    {
        return Flow(
            static () => PullRequest(),
            static () => Json(AUTO_MERGE_OFF),
            static () => Json(DRAFT),
            static () => Posted()
        );
    }

    private void BoardWriteThrows(Exception exception)
    {
        this._boardWriter.SetItemStatusAsync(
                owner: Arg.Any<string>(),
                repo: Arg.Any<string>(),
                kind: Arg.Any<WorkItemKind>(),
                number: Arg.Any<int>(),
                project: Arg.Any<WorkflowProject>(),
                fieldId: Arg.Any<string>(),
                fieldName: Arg.Any<string>(),
                optionId: Arg.Any<string>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(exception);
    }

    private PrTakeBackService CreateService(RecordingHttpMessageHandler upstream)
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            options: GitHubOptionsWith(),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        GitHubGraphQlClient graphQl = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            options: GitHubOptionsWith(),
            logger: this.GetTypedLogger<GitHubGraphQlClient>()
        );

        IWorkflowStatusResolver statusResolver = GetSubstitute<IWorkflowStatusResolver>();
        statusResolver
            .ResolveAsync(
                repository: Arg.Any<string>(),
                project: Project,
                state: WorkflowStates.DEVELOPMENT,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(new ResolvedStatusOption(FieldId: FIELD_ID, FieldName: FIELD_NAME, OptionId: OPTION_ID));

        return new(
            restClient: rest,
            graphQlClient: graphQl,
            leakChecker: this._leakChecker,
            boardResolver: this._boardResolver,
            statusResolver: statusResolver,
            boardWriter: this._boardWriter
        );
    }

    private Task<PrTakeBackResult> TakeBackAsync(
        RecordingHttpMessageHandler upstream,
        string repository = REPOSITORY,
        int pullRequest = PR,
        string body = BODY
    )
    {
        return this.CreateService(upstream)
            .TakeBackAsync(
                repository: repository,
                pullRequest: pullRequest,
                body: body,
                cancellationToken: this.CancellationToken()
            )
            .AsTask();
    }

    private Task ReceivedBoardWriteAsync()
    {
        return this
            ._boardWriter.Received(1)
            .SetItemStatusAsync(
                owner: OWNER,
                repo: NAME,
                kind: WorkItemKind.PULL_REQUEST,
                number: PR,
                project: Project,
                fieldId: FIELD_ID,
                fieldName: FIELD_NAME,
                optionId: OPTION_ID,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .AsTask();
    }

    private Task DidNotWriteTheBoardAsync()
    {
        return this
            ._boardWriter.DidNotReceiveWithAnyArgs()
            .SetItemStatusAsync(
                owner: default!,
                repo: default!,
                kind: default,
                number: default,
                project: default!,
                fieldId: default!,
                fieldName: default!,
                optionId: default!,
                cancellationToken: default
            )
            .AsTask();
    }

    private static int Count(RecordingHttpMessageHandler upstream, HttpMethod method, Uri uri)
    {
        int count = 0;

        foreach (RecordedRequest request in upstream.Requests)
        {
            if (request.Method == method && request.RequestUri == uri)
            {
                ++count;
            }
        }

        return count;
    }

    private static void AssertNothingWritten(RecordingHttpMessageHandler upstream)
    {
        Assert.DoesNotContain(upstream.Requests, request => request.Method != HttpMethod.Get);
    }

    private static void AssertNoComment(RecordingHttpMessageHandler upstream)
    {
        Assert.Equal(expected: 0, actual: Count(upstream: upstream, method: HttpMethod.Post, uri: CommentsUri));
    }

    private static void AssertHas(string expected, string? actual)
    {
        Assert.Contains(expectedSubstring: expected, actualString: actual, comparisonType: StringComparison.Ordinal);
    }

    private static void AssertRefused(PrTakeBackResult result, string subject, string rule, int? lineNumber = null)
    {
        Assert.Equal(expected: PrTakeBackOutcome.Refused, actual: result.Outcome);
        Assert.Equal(expected: rule, actual: result.Rule);
        Assert.Equal(expected: lineNumber, actual: result.LineNumber);
        Assert.StartsWith(
            expectedStartString: subject + " refused: " + rule,
            actualString: result.Message,
            comparisonType: StringComparison.Ordinal
        );
        Assert.Null(result.CommentUrl);
        Assert.Null(result.AutoMergeWasOn);
        Assert.Null(result.AlreadyDraft);
        Assert.Null(result.StepsDone);
    }

    [Fact]
    public async Task APullRequestWithAutoMergeOnIsTakenBackWithEveryWriteInOrderAsync()
    {
        // The board-failure tests show the board is written after the draft and before the comment
        using RecordingHttpMessageHandler upstream = FullFlow();

        PrTakeBackResult result = await this.TakeBackAsync(upstream);

        Assert.Equal(expected: PrTakeBackOutcome.TakenBack, actual: result.Outcome);
        Assert.Equal(
            expected: REPOSITORY + "#93 was taken back: auto-merge off, draft, Development",
            actual: result.Message
        );
        Assert.Equal(expected: new Uri(COMMENT_URL), actual: result.CommentUrl);
        Assert.True(result.AutoMergeWasOn);
        Assert.False(result.AlreadyDraft);
        Assert.Equal(expected: [STEP_AUTO_MERGE, STEP_DRAFT, STEP_BOARD, STEP_COMMENT], actual: result.StepsDone);
        Assert.Null(result.Rule);
        Assert.Null(result.LineNumber);
        await this.ReceivedBoardWriteAsync();

        IReadOnlyList<RecordedRequest> requests = upstream.Requests;
        Assert.Equal(expected: 4, actual: requests.Count);
        Assert.Equal(expected: HttpMethod.Get, actual: requests[0].Method);
        Assert.Equal(expected: PullUri, actual: requests[0].RequestUri);
        Assert.Equal(expected: GraphQlUri, actual: requests[1].RequestUri);
        AssertHas(expected: "disablePullRequestAutoMerge", actual: requests[1].Body);
        AssertHas(expected: NODE_ID, actual: requests[1].Body);
        Assert.Equal(expected: GraphQlUri, actual: requests[2].RequestUri);
        AssertHas(expected: "convertPullRequestToDraft", actual: requests[2].Body);
        AssertHas(expected: NODE_ID, actual: requests[2].Body);
        Assert.Equal(expected: HttpMethod.Post, actual: requests[3].Method);
        Assert.Equal(expected: CommentsUri, actual: requests[3].RequestUri);
        AssertHas(expected: BODY, actual: requests[3].Body);
    }

    [Fact]
    public async Task ADraftWithAutoMergeOffSkipsBothMutationsButStillMovesTheBoardAndCommentsAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => PullRequest(draft: true, autoMerge: false),
            static () => Posted()
        );

        PrTakeBackResult result = await this.TakeBackAsync(upstream);

        Assert.Equal(expected: PrTakeBackOutcome.TakenBack, actual: result.Outcome);
        Assert.False(result.AutoMergeWasOn);
        Assert.True(result.AlreadyDraft);
        Assert.Equal(expected: [STEP_AUTO_MERGE, STEP_DRAFT, STEP_BOARD, STEP_COMMENT], actual: result.StepsDone);
        Assert.Equal(expected: 0, actual: Count(upstream: upstream, method: HttpMethod.Post, uri: GraphQlUri));
        Assert.Equal(expected: 1, actual: Count(upstream: upstream, method: HttpMethod.Post, uri: CommentsUri));
        await this.ReceivedBoardWriteAsync();
    }

    [Fact]
    public async Task ADraftWithAutoMergeOnOnlyTurnsAutoMergeOffAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => PullRequest(draft: true),
            static () => Json(AUTO_MERGE_OFF),
            static () => Posted()
        );

        PrTakeBackResult result = await this.TakeBackAsync(upstream);

        Assert.Equal(expected: PrTakeBackOutcome.TakenBack, actual: result.Outcome);
        Assert.True(result.AutoMergeWasOn);
        Assert.True(result.AlreadyDraft);
        Assert.Equal(expected: 1, actual: Count(upstream: upstream, method: HttpMethod.Post, uri: GraphQlUri));
        AssertHas(expected: "disablePullRequestAutoMerge", actual: upstream.Requests[1].Body);
    }

    [Fact]
    public async Task AReadyPullRequestWithAutoMergeOffIsOnlyConvertedToDraftAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => PullRequest(autoMerge: false),
            static () => Json(DRAFT),
            static () => Posted()
        );

        PrTakeBackResult result = await this.TakeBackAsync(upstream);

        Assert.Equal(expected: PrTakeBackOutcome.TakenBack, actual: result.Outcome);
        Assert.False(result.AutoMergeWasOn);
        Assert.False(result.AlreadyDraft);
        Assert.Equal(expected: 1, actual: Count(upstream: upstream, method: HttpMethod.Post, uri: GraphQlUri));
        AssertHas(expected: "convertPullRequestToDraft", actual: upstream.Requests[1].Body);
    }

    [Fact]
    public async Task ABodyThatLeaksIsRefusedWithItsLineAsync()
    {
        this._leakChecker.CheckBody(BODY)
            .Returns(new LeakRefusal(Subject: LeakSubject.BODY, Rule: LeakRule.TOKEN, LineNumber: 3));
        using RecordingHttpMessageHandler upstream = Flow();

        PrTakeBackResult result = await this.TakeBackAsync(upstream);

        AssertRefused(result: result, subject: "body", rule: "token", lineNumber: 3);
        Assert.Equal(expected: "body refused: token (line 3)", actual: result.Message);
        Assert.Empty(upstream.Requests);
        await this.DidNotWriteTheBoardAsync();
    }

    [Fact]
    public async Task ABodyThatNamesAnotherRepositoryIsRefusedAsync()
    {
        this._leakChecker.CheckRepositoryReferencesAsync(
                repository: REPOSITORY,
                subject: LeakSubject.BODY,
                text: BODY,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(new LeakRefusal(Subject: LeakSubject.BODY, Rule: LeakRule.TOKEN, LineNumber: 1));
        using RecordingHttpMessageHandler upstream = Flow();

        PrTakeBackResult result = await this.TakeBackAsync(upstream);

        AssertRefused(result: result, subject: "body", rule: "token", lineNumber: 1);
        Assert.Empty(upstream.Requests);
        await this.DidNotWriteTheBoardAsync();
    }

    [Fact]
    public async Task ABodyWithAnApprovalKeywordIsRefusedAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow();

        PrTakeBackResult result = await this.TakeBackAsync(upstream: upstream, body: "Taking back\n\nLGTM");

        AssertRefused(result: result, subject: "body", rule: "approval keyword", lineNumber: 3);
        Assert.Empty(upstream.Requests);
        await this.DidNotWriteTheBoardAsync();
    }

    [Fact]
    public async Task AClosedPullRequestIsRefusedAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(static () => PullRequest(state: "closed"));

        PrTakeBackResult result = await this.TakeBackAsync(upstream);

        AssertRefused(result: result, subject: "pull request", rule: "not open");
        AssertNothingWritten(upstream);
        await this.DidNotWriteTheBoardAsync();
    }

    [Fact]
    public async Task AMissingPullRequestIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(static () => Responses.Status(HttpStatusCode.NotFound));

        PrTakeBackResult result = await this.TakeBackAsync(upstream);

        Assert.Equal(expected: PrTakeBackOutcome.NotFound, actual: result.Outcome);
        Assert.Equal(
            expected: "the pull request " + REPOSITORY + "#93 was not found, or the caller cannot see it",
            actual: result.Message
        );
        Assert.Null(result.StepsDone);
        AssertNothingWritten(upstream);
        await this.DidNotWriteTheBoardAsync();
    }

    [Theory]
    [InlineData("{\"state\":\"open\",\"draft\":false,\"auto_merge\":null}")]
    [InlineData("{\"node_id\":\"\",\"state\":\"open\",\"draft\":false,\"auto_merge\":null}")]
    [InlineData("{\"node_id\":\"PR_1\",\"draft\":false,\"auto_merge\":null}")]
    [InlineData("{\"node_id\":\"PR_1\",\"state\":\"open\",\"auto_merge\":null}")]
    [InlineData("{\"node_id\":\"PR_1\",\"state\":\"open\",\"draft\":false}")]
    [InlineData("{\"node_id\":\"PR_1\",\"state\":\"open\",\"draft\":false,\"auto_merge\":true}")]
    [InlineData("{\"node_id\":\"PR_1\",\"state\":\"open\",\"draft\":false,\"auto_merge\":\"on\"}")]
    public Task AnIncompletePullRequestIsNotTakenBackAsync(string json)
    {
        return this.AssertPullRequestReadFailsAsync(() => Json(json));
    }

    [Fact]
    public Task APullRequestReadThatFailsIsNotTakenBackAsync()
    {
        return this.AssertPullRequestReadFailsAsync(static () => Responses.Status(HttpStatusCode.InternalServerError));
    }

    [Fact]
    public Task APullRequestReadWithANullBodyIsNotTakenBackAsync()
    {
        return this.AssertPullRequestReadFailsAsync(static () => Json("null"));
    }

    [Fact]
    public Task APullRequestReadThatCannotReachGitHubIsNotTakenBackAsync()
    {
        return this.AssertPullRequestReadFailsAsync(static () => TransportFails());
    }

    private async Task AssertPullRequestReadFailsAsync(Func<HttpResponseMessage> response)
    {
        using RecordingHttpMessageHandler upstream = Flow(response);

        PrTakeBackResult result = await this.TakeBackAsync(upstream);

        Assert.Equal(expected: PrTakeBackOutcome.NotTakenBack, actual: result.Outcome);
        Assert.Equal(
            expected: REPOSITORY + "#93 was not taken back: could not read the pull request",
            actual: result.Message
        );
        Assert.Equal(expected: [], actual: result.StepsDone);
        Assert.Single(upstream.Requests);
        await this.DidNotWriteTheBoardAsync();
    }

    [Fact]
    public async Task ARefusedAutoMergeOffStopsBeforeAnythingElseAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(static () => PullRequest(), static () => Json(GRAPHQL_ERROR));

        PrTakeBackResult result = await this.TakeBackAsync(upstream);

        Assert.Equal(expected: PrTakeBackOutcome.NotTakenBack, actual: result.Outcome);
        Assert.StartsWith(
            expectedStartString: REPOSITORY + "#93 was not taken back: step failed: auto-merge off (",
            actualString: result.Message,
            comparisonType: StringComparison.Ordinal
        );
        Assert.Equal(expected: [], actual: result.StepsDone);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
        AssertNoComment(upstream);
        await this.DidNotWriteTheBoardAsync();
    }

    [Theory]
    [InlineData(
        """{"data":{"disablePullRequestAutoMerge":{"pullRequest":{"autoMergeRequest":{"enabledAt":"2026-11-07T00:00:00Z"}}}}}"""
    )]
    [InlineData("""{"data":{"disablePullRequestAutoMerge":{"pullRequest":null}}}""")]
    [InlineData("""{"data":{"disablePullRequestAutoMerge":null}}""")]
    public async Task AnUnconfirmedAutoMergeOffMayHaveBeenTakenBackAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = Flow(static () => PullRequest(), () => Json(json));

        PrTakeBackResult result = await this.TakeBackAsync(upstream);

        Assert.Equal(expected: PrTakeBackOutcome.MayHaveBeenTakenBack, actual: result.Outcome);
        Assert.Equal(
            expected: "could not take back "
                + REPOSITORY
                + "#93, whose last step may have been made (the answer from GitHub could not be read); read the pull request before trying again",
            actual: result.Message
        );
        Assert.Equal(expected: [], actual: result.StepsDone);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
        AssertNoComment(upstream);
        await this.DidNotWriteTheBoardAsync();
    }

    [Fact]
    public async Task ARefusedDraftStopsBeforeTheBoardAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => PullRequest(),
            static () => Json(AUTO_MERGE_OFF),
            static () => Json(GRAPHQL_ERROR)
        );

        PrTakeBackResult result = await this.TakeBackAsync(upstream);

        Assert.Equal(expected: PrTakeBackOutcome.NotTakenBack, actual: result.Outcome);
        Assert.StartsWith(
            expectedStartString: REPOSITORY + "#93 was not taken back: step failed: draft (",
            actualString: result.Message,
            comparisonType: StringComparison.Ordinal
        );
        Assert.Equal(expected: [STEP_AUTO_MERGE], actual: result.StepsDone);
        Assert.Equal(expected: 3, actual: upstream.Requests.Count);
        AssertNoComment(upstream);
        await this.DidNotWriteTheBoardAsync();
    }

    [Theory]
    [InlineData("""{"data":{"convertPullRequestToDraft":{"pullRequest":{"isDraft":false}}}}""")]
    [InlineData("""{"data":{"convertPullRequestToDraft":{"pullRequest":{"isDraft":null}}}}""")]
    [InlineData("""{"data":{"convertPullRequestToDraft":{"pullRequest":null}}}""")]
    [InlineData("""{"data":{"convertPullRequestToDraft":null}}""")]
    public async Task AnUnconfirmedDraftMayHaveBeenTakenBackAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => PullRequest(),
            static () => Json(AUTO_MERGE_OFF),
            () => Json(json)
        );

        PrTakeBackResult result = await this.TakeBackAsync(upstream);

        Assert.Equal(expected: PrTakeBackOutcome.MayHaveBeenTakenBack, actual: result.Outcome);
        Assert.Equal(expected: [STEP_AUTO_MERGE], actual: result.StepsDone);
        AssertNoComment(upstream);
        await this.DidNotWriteTheBoardAsync();
    }

    [Fact]
    public async Task ABoardWriteThatIsNotConfirmedMayHaveBeenTakenBackAsync()
    {
        this.BoardWriteThrows(new BoardWriteException("the board write was refused"));
        using RecordingHttpMessageHandler upstream = Flow(
            static () => PullRequest(),
            static () => Json(AUTO_MERGE_OFF),
            static () => Json(DRAFT)
        );

        PrTakeBackResult result = await this.TakeBackAsync(upstream);

        Assert.Equal(expected: PrTakeBackOutcome.MayHaveBeenTakenBack, actual: result.Outcome);
        Assert.Contains(
            expectedSubstring: "the Workflow board did not confirm the change",
            actualString: result.Message,
            comparisonType: StringComparison.Ordinal
        );
        Assert.Equal(expected: [STEP_AUTO_MERGE, STEP_DRAFT], actual: result.StepsDone);
        Assert.Equal(expected: 3, actual: upstream.Requests.Count);
        AssertNoComment(upstream);
    }

    [Fact]
    public async Task ABoardWriteRefusedByGitHubIsNotTakenBackAsync()
    {
        this.BoardWriteThrows(new BoardWriteException("refused", new GitHubGraphQlException("Not found")));
        using RecordingHttpMessageHandler upstream = Flow(
            static () => PullRequest(),
            static () => Json(AUTO_MERGE_OFF),
            static () => Json(DRAFT)
        );

        PrTakeBackResult result = await this.TakeBackAsync(upstream);

        Assert.Equal(expected: PrTakeBackOutcome.NotTakenBack, actual: result.Outcome);
        Assert.Equal(
            expected: REPOSITORY
                + "#93 was not taken back: step failed: Workflow board set to Development (GitHub refused the change)",
            actual: result.Message
        );
        Assert.Equal(expected: [STEP_AUTO_MERGE, STEP_DRAFT], actual: result.StepsDone);
        AssertNoComment(upstream);
    }

    [Fact]
    public async Task ARefusedCommentIsNotTakenBackAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => PullRequest(),
            static () => Json(AUTO_MERGE_OFF),
            static () => Json(DRAFT),
            static () => Responses.Status(HttpStatusCode.Forbidden)
        );

        PrTakeBackResult result = await this.TakeBackAsync(upstream);

        Assert.Equal(expected: PrTakeBackOutcome.NotTakenBack, actual: result.Outcome);
        Assert.Equal(
            expected: REPOSITORY
                + "#93 was not taken back: step failed: comment posted (GitHub answered with status 403)",
            actual: result.Message
        );
        Assert.Equal(expected: [STEP_AUTO_MERGE, STEP_DRAFT, STEP_BOARD], actual: result.StepsDone);
        Assert.Null(result.CommentUrl);
        await this.ReceivedBoardWriteAsync();
    }

    [Fact]
    public async Task ACommentThatCannotReachGitHubMayHaveBeenTakenBackAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => PullRequest(draft: true, autoMerge: false),
            static () => TransportFails()
        );

        PrTakeBackResult result = await this.TakeBackAsync(upstream);

        Assert.Equal(expected: PrTakeBackOutcome.MayHaveBeenTakenBack, actual: result.Outcome);
        Assert.Equal(
            expected: "could not take back "
                + REPOSITORY
                + "#93, whose last step may have been made (the request to GitHub failed); read the pull request before trying again",
            actual: result.Message
        );
        Assert.Equal(expected: [STEP_AUTO_MERGE, STEP_DRAFT, STEP_BOARD], actual: result.StepsDone);
    }

    [Fact]
    public async Task ACommentWithoutAUrlMayHaveBeenTakenBackAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => PullRequest(draft: true, autoMerge: false),
            static () => Responses.StatusWithJson(statusCode: HttpStatusCode.Created, json: "{\"id\":555}")
        );

        PrTakeBackResult result = await this.TakeBackAsync(upstream);

        Assert.Equal(expected: PrTakeBackOutcome.MayHaveBeenTakenBack, actual: result.Outcome);
        Assert.Contains(
            expectedSubstring: "(the answer from GitHub could not be read)",
            actualString: result.Message,
            comparisonType: StringComparison.Ordinal
        );
        Assert.Equal(expected: [STEP_AUTO_MERGE, STEP_DRAFT, STEP_BOARD], actual: result.StepsDone);
    }

    [Theory]
    [InlineData("https://github.com/" + REPOSITORY + "/pull/94#issuecomment-555")]
    [InlineData("http://github.com/" + REPOSITORY + "/pull/93#issuecomment-555")]
    [InlineData("https://github.com/" + REPOSITORY + "/pull/93")]
    public async Task AnUnexpectedCommentUrlMayHaveBeenTakenBackAsync(string url)
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => PullRequest(draft: true, autoMerge: false),
            () => Posted(url: url)
        );

        PrTakeBackResult result = await this.TakeBackAsync(upstream);

        Assert.Equal(expected: PrTakeBackOutcome.MayHaveBeenTakenBack, actual: result.Outcome);
        Assert.Contains(
            expectedSubstring: "(the answer from GitHub could not be read)",
            actualString: result.Message,
            comparisonType: StringComparison.Ordinal
        );
        Assert.Null(result.CommentUrl);
        Assert.Equal(expected: [STEP_AUTO_MERGE, STEP_DRAFT, STEP_BOARD], actual: result.StepsDone);
    }

    [Theory]
    [InlineData("credfeto")]
    [InlineData("credfeto/")]
    [InlineData("")]
    public async Task AnInvalidRepositoryIsAUsageErrorAsync(string repository)
    {
        using RecordingHttpMessageHandler upstream = Flow();

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.TakeBackAsync(upstream: upstream, repository: repository)
        );

        Assert.Equal(expected: "repository", actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task APullRequestNumberThatIsNotPositiveIsAUsageErrorAsync(int pullRequest)
    {
        using RecordingHttpMessageHandler upstream = Flow();

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.TakeBackAsync(upstream: upstream, pullRequest: pullRequest)
        );

        Assert.Equal(expected: "pr", actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \n\t")]
    [InlineData("​")]
    public async Task AnEmptyBodyIsAUsageErrorAsync(string body)
    {
        using RecordingHttpMessageHandler upstream = Flow();

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.TakeBackAsync(upstream: upstream, body: body)
        );

        Assert.Equal(expected: "body", actual: exception.ParameterName);
        Assert.Equal(expected: "the body is empty", actual: exception.Message);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task AFailedBoardResolveWritesNothingAsync()
    {
        this._boardResolver.ResolveAsync(repository: Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>())
            .ThrowsAsync(new WorkflowBoardNotFoundException("no board"));
        using RecordingHttpMessageHandler upstream = Flow();

        await Assert.ThrowsAsync<WorkflowBoardNotFoundException>(() => this.TakeBackAsync(upstream));

        Assert.Empty(upstream.Requests);
        await this.DidNotWriteTheBoardAsync();
    }
}
