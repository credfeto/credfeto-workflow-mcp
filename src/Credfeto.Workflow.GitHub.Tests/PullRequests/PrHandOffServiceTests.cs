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
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.PullRequests;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.PullRequests;

public sealed class PrHandOffServiceTests : GitHubClientTestBase
{
    private const string OWNER = "credfeto";
    private const string NAME = "credfeto-workflow-mcp";
    private const string REPOSITORY = OWNER + "/" + NAME;
    private const int PR = 93;
    private const string TITLE = "feat(mcp): add pr hand off";
    private const string DESCRIPTION = "## Summary\n\nHand off a pull request.\n\nCloses #55";
    private const string RECORD = "Code review clean; security review clean.";
    private const string HEAD_SHA = "0123456789abcdef0123456789abcdef01234567";
    private const string OTHER_SHA = "89abcdef0123456789abcdef0123456789abcdef";
    private const string NODE_ID = "PR_kwDO1";
    private const string FIELD_ID = "PVTSSF_1";
    private const string FIELD_NAME = "Status";
    private const string OPTION_ID = "OPT_1";
    private const string RECORD_URL = "https://github.com/" + REPOSITORY + "/pull/93#issuecomment-555";

    private const string READY = """{"data":{"markPullRequestReadyForReview":{"pullRequest":{"isDraft":false}}}}""";

    private const string AUTO_MERGE_ON =
        """{"data":{"enablePullRequestAutoMerge":{"pullRequest":{"autoMergeRequest":{"enabledAt":"2026-11-07T00:00:00Z"}}}}}""";

    private const string GRAPHQL_ERROR = """{"errors":[{"message":"Not found"}],"data":null}""";

    private static readonly WorkflowProject Project = new(ProjectNodeId: "PVT_1", Number: 7, Owner: OWNER);

    private static readonly Uri PullUri = new(GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/pulls/93");

    private static readonly Uri RepositoryUri = new(GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY);

    private static readonly Uri BaseBranchUri = new(
        GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/branches/main"
    );

    private static readonly Uri RulesUri = new(
        GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/rules/branches/main?per_page=100"
    );

    private static readonly Uri CommentsUri = new(
        GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/issues/93/comments"
    );

    private static readonly Uri GraphQlUri = new(GitHubServices.ApiBaseUrl + "graphql");

    private readonly IWorkflowBoardResolver _boardResolver;
    private readonly IBoardWriter _boardWriter;
    private readonly ILeakChecker _leakChecker;
    private readonly IPrChecksService _prChecks;

    public PrHandOffServiceTests()
    {
        this._leakChecker = GetSubstitute<ILeakChecker>();
        this._boardWriter = GetSubstitute<IBoardWriter>();
        this._prChecks = GetSubstitute<IPrChecksService>();
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
        this.Checks(verdict: PrChecksVerdict.Passed, sha: HEAD_SHA);
    }

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private static RecordingHttpMessageHandler Flow(params Func<HttpResponseMessage>[] responses)
    {
        return new(index => index < responses.Length ? responses[index]() : Unexpected(index));
    }

    private static HttpResponseMessage PullRequest(
        bool draft = true,
        string state = "open",
        string sha = HEAD_SHA,
        string labels = ""
    )
    {
        string draftText = draft ? "true" : "false";

        return Responses.Json(
            "{\"node_id\":\""
                + NODE_ID
                + "\",\"state\":\""
                + state
                + "\",\"draft\":"
                + draftText
                + ",\"labels\":["
                + labels
                + "],\"head\":{\"ref\":\"feat/55-pr-hand-off-tool\",\"sha\":\""
                + sha
                + "\"},\"base\":{\"ref\":\"main\",\"sha\":\""
                + OTHER_SHA
                + "\"}}"
        );
    }

    private static HttpResponseMessage AllowsAutoMerge(bool allowed = true)
    {
        return Responses.Json(allowed ? "{\"allow_auto_merge\":true}" : "{\"allow_auto_merge\":false}");
    }

    private static HttpResponseMessage BaseBranch(bool isProtected)
    {
        return Responses.Json(isProtected ? "{\"protected\":true}" : "{\"protected\":false}");
    }

    private static HttpResponseMessage Updated()
    {
        return Responses.Json("{\"number\":93,\"title\":\"" + TITLE + "\"}");
    }

    private static HttpResponseMessage Posted(string url = RECORD_URL)
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

    // A draft on a protected base that allows auto-merge: every write is made
    private static RecordingHttpMessageHandler FullFlow()
    {
        return Flow(
            static () => PullRequest(),
            static () => AllowsAutoMerge(),
            static () => BaseBranch(isProtected: true),
            static () => Updated(),
            static () => Json(READY),
            static () => Posted(),
            static () => PullRequest(),
            static () => Json(AUTO_MERGE_ON)
        );
    }

    private void Checks(PrChecksVerdict verdict, string sha)
    {
        this._prChecks.ReadAsync(
                repository: REPOSITORY,
                pullRequest: PR,
                sha: null,
                branch: null,
                all: false,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(
                new PrChecksResponse(
                    Repo: REPOSITORY,
                    Sha: sha,
                    Verdict: verdict,
                    Passed: 0,
                    Failed: 0,
                    Pending: 0,
                    Checks: []
                )
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

    private PrHandOffService CreateService(RecordingHttpMessageHandler upstream)
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        GitHubGraphQlClient graphQl = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            logger: this.GetTypedLogger<GitHubGraphQlClient>()
        );

        IWorkflowStatusResolver statusResolver = GetSubstitute<IWorkflowStatusResolver>();
        statusResolver
            .ResolveAsync(
                repository: Arg.Any<string>(),
                project: Project,
                state: WorkflowStates.HUMAN_REVIEW,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(new ResolvedStatusOption(FieldId: FIELD_ID, FieldName: FIELD_NAME, OptionId: OPTION_ID));

        return new(
            restClient: rest,
            graphQlClient: graphQl,
            leakChecker: this._leakChecker,
            boardResolver: this._boardResolver,
            statusResolver: statusResolver,
            boardWriter: this._boardWriter,
            prChecks: this._prChecks
        );
    }

    private Task<PrHandOffResult> HandOffAsync(
        RecordingHttpMessageHandler upstream,
        string repository = REPOSITORY,
        int pullRequest = PR,
        string title = TITLE,
        string description = DESCRIPTION,
        string? record = RECORD
    )
    {
        return this.CreateService(upstream)
            .HandOffAsync(
                repository: repository,
                pullRequest: pullRequest,
                title: title,
                description: description,
                record: record,
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

    private static int IndexOf(RecordingHttpMessageHandler upstream, HttpMethod method, Uri uri, int after = -1)
    {
        IReadOnlyList<RecordedRequest> requests = upstream.Requests;

        for (int index = after + 1; index < requests.Count; index++)
        {
            if (requests[index].Method == method && requests[index].RequestUri == uri)
            {
                return index;
            }
        }

        return -1;
    }

    private static void AssertNothingWritten(RecordingHttpMessageHandler upstream)
    {
        Assert.DoesNotContain(upstream.Requests, request => request.Method != HttpMethod.Get);
    }

    private static void AssertRefused(PrHandOffResult result, string subject, string rule, int? lineNumber = null)
    {
        Assert.Equal(expected: PrHandOffOutcome.Refused, actual: result.Outcome);
        Assert.Equal(expected: rule, actual: result.Rule);
        Assert.Equal(expected: lineNumber, actual: result.LineNumber);
        Assert.StartsWith(
            expectedStartString: subject + " refused: " + rule,
            actualString: result.Message,
            comparisonType: StringComparison.Ordinal
        );
        Assert.Null(result.HeadSha);
        Assert.Null(result.ReviewRecordUrl);
        Assert.Null(result.AutoMerge);
        Assert.Null(result.StepsDone);
    }

    [Fact]
    public async Task ADraftIsHandedOffWithEveryWriteInOrderAsync()
    {
        using RecordingHttpMessageHandler upstream = FullFlow();

        PrHandOffResult result = await this.HandOffAsync(upstream);

        Assert.Equal(expected: PrHandOffOutcome.HandedOff, actual: result.Outcome);
        Assert.Equal(expected: PrHandOffAutoMerge.On, actual: result.AutoMerge);
        Assert.Null(result.AutoMergeReason);
        Assert.Equal(expected: HEAD_SHA, actual: result.HeadSha);
        Assert.Equal(expected: new Uri(RECORD_URL), actual: result.ReviewRecordUrl);
        Assert.Null(result.StepsDone);
        Assert.Equal(
            expected: REPOSITORY + "#93 was handed off for human review; auto-merge on",
            actual: result.Message
        );
        await this.ReceivedBoardWriteAsync();

        int patch = IndexOf(upstream: upstream, method: HttpMethod.Patch, uri: PullUri);
        int ready = IndexOf(upstream: upstream, method: HttpMethod.Post, uri: GraphQlUri);
        int record = IndexOf(upstream: upstream, method: HttpMethod.Post, uri: CommentsUri);
        int reRead = IndexOf(upstream: upstream, method: HttpMethod.Get, uri: PullUri, after: record);
        int autoMerge = IndexOf(upstream: upstream, method: HttpMethod.Post, uri: GraphQlUri, after: ready);

        Assert.Equal(expected: 8, actual: upstream.Requests.Count);
        Assert.True(patch > 0, userMessage: "the title and description are set after the reads");
        Assert.True(ready > patch, userMessage: "ready follows the title and description");
        Assert.True(record > ready, userMessage: "the record follows ready");
        Assert.True(reRead > record, userMessage: "the head is read again after the record");
        Assert.True(autoMerge > reRead, userMessage: "auto-merge follows the second read");
    }

    [Fact]
    public async Task TheWritesCarryTheTitleDescriptionAndRecordAsync()
    {
        using RecordingHttpMessageHandler upstream = FullFlow();

        await this.HandOffAsync(upstream);

        string? patch = upstream.Requests[3].Body;
        AssertHas(expected: "\"title\":\"" + TITLE + "\"", actual: patch);
        AssertHas(expected: "Closes #55", actual: patch);

        string? ready = upstream.Requests[4].Body;
        AssertHas(expected: "markPullRequestReadyForReview", actual: ready);
        AssertHas(expected: NODE_ID, actual: ready);

        string? comment = upstream.Requests[5].Body;
        AssertHas(expected: "### AI Review Loop: reviewed " + HEAD_SHA, actual: comment);
        AssertHas(expected: RECORD, actual: comment);

        string? autoMerge = upstream.Requests[7].Body;
        AssertHas(expected: "mergeMethod:MERGE", actual: autoMerge);
        AssertHas(expected: HEAD_SHA, actual: autoMerge);
    }

    private static void AssertHas(string expected, string? actual)
    {
        Assert.Contains(expectedSubstring: expected, actualString: actual, comparisonType: StringComparison.Ordinal);
    }

    private static bool Has(string? body, string text)
    {
        return body?.Contains(value: text, comparisonType: StringComparison.Ordinal) is true;
    }

    [Fact]
    public async Task WithoutARecordTheReviewRecordIsTheHeadingAloneAsync()
    {
        using RecordingHttpMessageHandler upstream = FullFlow();

        PrHandOffResult result = await this.HandOffAsync(upstream: upstream, record: null);

        Assert.Equal(expected: PrHandOffOutcome.HandedOff, actual: result.Outcome);
        AssertHas(
            expected: "\"body\":\"### AI Review Loop: reviewed " + HEAD_SHA + "\"",
            actual: upstream.Requests[5].Body
        );
    }

    [Fact]
    public async Task AReadyPullRequestIsNotMarkedReadyAgainAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => PullRequest(draft: false),
            static () => AllowsAutoMerge(),
            static () => BaseBranch(isProtected: true),
            static () => Updated(),
            static () => Posted(),
            static () => PullRequest(draft: false),
            static () => Json(AUTO_MERGE_ON)
        );

        PrHandOffResult result = await this.HandOffAsync(upstream);

        Assert.Equal(expected: PrHandOffOutcome.HandedOff, actual: result.Outcome);
        Assert.Equal(expected: PrHandOffAutoMerge.On, actual: result.AutoMerge);
        Assert.Single(upstream.Requests, request => request.RequestUri == GraphQlUri);
        Assert.DoesNotContain(
            upstream.Requests,
            request => Has(body: request.Body, text: "markPullRequestReadyForReview")
        );
    }

    [Fact]
    public async Task ARepositoryWithoutAutoMergeIsHandedOffWithAutoMergeUnavailableAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => PullRequest(draft: false),
            static () => AllowsAutoMerge(allowed: false),
            static () => Updated(),
            static () => Posted()
        );

        PrHandOffResult result = await this.HandOffAsync(upstream);

        Assert.Equal(expected: PrHandOffOutcome.HandedOff, actual: result.Outcome);
        Assert.Equal(expected: PrHandOffAutoMerge.Unavailable, actual: result.AutoMerge);
        Assert.Equal(expected: "the repository does not allow auto-merge", actual: result.AutoMergeReason);
        Assert.Equal(
            expected: REPOSITORY
                + "#93 was handed off for human review; auto-merge not turned on: the repository does not allow auto-merge",
            actual: result.Message
        );
        Assert.Equal(expected: 4, actual: upstream.Requests.Count);
        Assert.Equal(expected: RepositoryUri, actual: upstream.Requests[1].RequestUri);
    }

    [Fact]
    public async Task ABaseThatNeedsNothingIsHandedOffWithAutoMergeNotArmedAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => PullRequest(draft: false),
            static () => AllowsAutoMerge(),
            static () => BaseBranch(isProtected: false),
            static () => Json("[{\"type\":\"deletion\"}]"),
            static () => Updated(),
            static () => Posted()
        );

        PrHandOffResult result = await this.HandOffAsync(upstream);

        Assert.Equal(expected: PrHandOffOutcome.HandedOff, actual: result.Outcome);
        Assert.Equal(expected: PrHandOffAutoMerge.NotArmed, actual: result.AutoMerge);
        Assert.Equal(expected: "the base branch requires no check and no review", actual: result.AutoMergeReason);
        Assert.Equal(expected: BaseBranchUri, actual: upstream.Requests[2].RequestUri);
        Assert.Equal(expected: RulesUri, actual: upstream.Requests[3].RequestUri);
        Assert.Equal(expected: 6, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task APrivateRepositoryWithoutRulesetsAndAnUnprotectedBaseIsNotArmedAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => PullRequest(draft: false),
            static () => AllowsAutoMerge(),
            static () => BaseBranch(isProtected: false),
            static () =>
                Responses.StatusWithJson(
                    statusCode: HttpStatusCode.Forbidden,
                    json: "{\"message\":\"Upgrade to GitHub Pro or make this repository public to enable this feature.\"}"
                ),
            static () => Updated(),
            static () => Posted()
        );

        PrHandOffResult result = await this.HandOffAsync(upstream);

        Assert.Equal(expected: PrHandOffOutcome.HandedOff, actual: result.Outcome);
        Assert.Equal(expected: PrHandOffAutoMerge.NotArmed, actual: result.AutoMerge);
    }

    [Theory]
    [InlineData("pull_request")]
    [InlineData("required_status_checks")]
    public async Task AnUnprotectedBaseWithARequiringRuleStillTurnsAutoMergeOnAsync(string rule)
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => PullRequest(draft: false),
            static () => AllowsAutoMerge(),
            static () => BaseBranch(isProtected: false),
            () => Json("[{\"type\":\"" + rule + "\"}]"),
            static () => Updated(),
            static () => Posted(),
            static () => PullRequest(draft: false),
            static () => Json(AUTO_MERGE_ON)
        );

        PrHandOffResult result = await this.HandOffAsync(upstream);

        Assert.Equal(expected: PrHandOffOutcome.HandedOff, actual: result.Outcome);
        Assert.Equal(expected: PrHandOffAutoMerge.On, actual: result.AutoMerge);
    }

    [Fact]
    public async Task AnUnreadableBaseIsTriedForAutoMergeAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => PullRequest(draft: false),
            static () => Responses.Status(HttpStatusCode.NotFound),
            static () => Responses.Status(HttpStatusCode.NotFound),
            static () => Updated(),
            static () => Posted(),
            static () => PullRequest(draft: false),
            static () => Json(AUTO_MERGE_ON)
        );

        PrHandOffResult result = await this.HandOffAsync(upstream);

        Assert.Equal(expected: PrHandOffOutcome.HandedOff, actual: result.Outcome);
        Assert.Equal(expected: PrHandOffAutoMerge.On, actual: result.AutoMerge);
    }

    [Theory]
    [InlineData("Add a hand-off tool")]
    [InlineData("feat:no space")]
    [InlineData("Feat(mcp): capitalised type")]
    public async Task ANonConventionalTitleIsRefusedBeforeAnyRequestAsync(string title)
    {
        using RecordingHttpMessageHandler upstream = Flow();

        PrHandOffResult result = await this.HandOffAsync(upstream: upstream, title: title);

        AssertRefused(result: result, subject: "title", rule: "Conventional Commits title");
        Assert.Empty(upstream.Requests);
        await this.DidNotWriteTheBoardAsync();
    }

    [Fact]
    public async Task ATitleThatLeaksIsRefusedAsync()
    {
        this._leakChecker.CheckValue(subject: LeakSubject.TITLE, value: TITLE)
            .Returns(new LeakRefusal(Subject: LeakSubject.TITLE, Rule: LeakRule.TOKEN, LineNumber: null));
        using RecordingHttpMessageHandler upstream = Flow();

        PrHandOffResult result = await this.HandOffAsync(upstream);

        AssertRefused(result: result, subject: "title", rule: "token");
        Assert.Empty(upstream.Requests);
        await this.DidNotWriteTheBoardAsync();
    }

    [Fact]
    public async Task ATitleThatNamesAnotherRepositoryIsRefusedAsync()
    {
        this._leakChecker.CheckRepositoryReferencesAsync(
                repository: REPOSITORY,
                subject: LeakSubject.TITLE,
                text: TITLE,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(new LeakRefusal(Subject: LeakSubject.TITLE, Rule: LeakRule.TOKEN, LineNumber: 1));
        using RecordingHttpMessageHandler upstream = Flow();

        PrHandOffResult result = await this.HandOffAsync(upstream);

        AssertRefused(result: result, subject: "title", rule: "token");
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task ADescriptionThatLeaksIsRefusedWithItsLineAsync()
    {
        this._leakChecker.CheckBody(DESCRIPTION)
            .Returns(new LeakRefusal(Subject: LeakSubject.BODY, Rule: LeakRule.TOKEN, LineNumber: 3));
        using RecordingHttpMessageHandler upstream = Flow();

        PrHandOffResult result = await this.HandOffAsync(upstream);

        AssertRefused(result: result, subject: "description", rule: "token", lineNumber: 3);
        Assert.Equal(expected: "description refused: token (line 3)", actual: result.Message);
        Assert.Empty(upstream.Requests);
        await this.DidNotWriteTheBoardAsync();
    }

    [Fact]
    public async Task ADescriptionWithAnApprovalKeywordIsRefusedAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow();

        PrHandOffResult result = await this.HandOffAsync(upstream: upstream, description: "Summary\n\nLGTM");

        AssertRefused(result: result, subject: "description", rule: "approval keyword", lineNumber: 3);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task ARecordThatLeaksIsRefusedAsync()
    {
        this._leakChecker.CheckRepositoryReferencesAsync(
                repository: REPOSITORY,
                subject: LeakSubject.BODY,
                text: RECORD,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(new LeakRefusal(Subject: LeakSubject.BODY, Rule: LeakRule.TOKEN, LineNumber: 1));
        using RecordingHttpMessageHandler upstream = Flow();

        PrHandOffResult result = await this.HandOffAsync(upstream);

        AssertRefused(result: result, subject: "record", rule: "token", lineNumber: 1);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task ARecordWithAnApprovalKeywordIsRefusedAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow();

        PrHandOffResult result = await this.HandOffAsync(upstream: upstream, record: "approved");

        AssertRefused(result: result, subject: "record", rule: "approval keyword", lineNumber: 1);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task AClosedPullRequestIsRefusedAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(static () => PullRequest(state: "closed"));

        PrHandOffResult result = await this.HandOffAsync(upstream);

        AssertRefused(result: result, subject: "pull request", rule: "not open");
        AssertNothingWritten(upstream);
        await this.DidNotWriteTheBoardAsync();
    }

    [Theory]
    [InlineData("Blocked", "Blocked label")]
    [InlineData("blocked", "Blocked label")]
    [InlineData("On Hold", "On Hold label")]
    public async Task AHeldPullRequestIsRefusedAsync(string label, string rule)
    {
        using RecordingHttpMessageHandler upstream = Flow(() =>
            PullRequest(labels: "{\"name\":\"enhancement\"},{\"name\":\"" + label + "\"}")
        );

        PrHandOffResult result = await this.HandOffAsync(upstream);

        AssertRefused(result: result, subject: "pull request", rule: rule);
        AssertNothingWritten(upstream);
        await this.DidNotWriteTheBoardAsync();
    }

    [Theory]
    [InlineData(PrChecksVerdict.Failed, "required checks failed")]
    [InlineData(PrChecksVerdict.Pending, "required checks pending")]
    [InlineData(PrChecksVerdict.NoChecks, "no checks")]
    public async Task RequiredChecksThatHaveNotPassedAreRefusedAsync(PrChecksVerdict verdict, string rule)
    {
        this.Checks(verdict: verdict, sha: HEAD_SHA);
        using RecordingHttpMessageHandler upstream = Flow(static () => PullRequest());

        PrHandOffResult result = await this.HandOffAsync(upstream);

        AssertRefused(result: result, subject: "pull request", rule: rule);
        AssertNothingWritten(upstream);
        await this.DidNotWriteTheBoardAsync();
    }

    [Fact]
    public async Task ChecksForAnotherHeadAreRefusedAsync()
    {
        this.Checks(verdict: PrChecksVerdict.Passed, sha: OTHER_SHA);
        using RecordingHttpMessageHandler upstream = Flow(static () => PullRequest());

        PrHandOffResult result = await this.HandOffAsync(upstream);

        AssertRefused(result: result, subject: "pull request", rule: "head changed");
        AssertNothingWritten(upstream);
        await this.DidNotWriteTheBoardAsync();
    }

    [Fact]
    public async Task AMissingPullRequestIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(static () => Responses.Status(HttpStatusCode.NotFound));

        PrHandOffResult result = await this.HandOffAsync(upstream);

        Assert.Equal(expected: PrHandOffOutcome.NotFound, actual: result.Outcome);
        Assert.Contains(
            expectedSubstring: REPOSITORY + "#93",
            actualString: result.Message,
            comparisonType: StringComparison.Ordinal
        );
        AssertNothingWritten(upstream);
        await this.DidNotWriteTheBoardAsync();
    }

    [Theory]
    [InlineData("{\"state\":\"open\"}")]
    [InlineData(
        "{\"node_id\":\"PR_1\",\"state\":\"open\",\"draft\":true,\"head\":{\"sha\":\"\"},\"base\":{\"ref\":\"main\"}}"
    )]
    public async Task AnUnreadablePullRequestIsNotHandedOffAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = Flow(() => Json(json));

        PrHandOffResult result = await this.HandOffAsync(upstream);

        Assert.Equal(expected: PrHandOffOutcome.NotHandedOff, actual: result.Outcome);
        Assert.Equal(
            expected: REPOSITORY + "#93 was not handed off: could not read the pull request",
            actual: result.Message
        );
        Assert.Equal(expected: [], actual: result.StepsDone);
        AssertNothingWritten(upstream);
        await this.DidNotWriteTheBoardAsync();
    }

    [Fact]
    public Task APullRequestReadThatFailsIsNotHandedOffAsync()
    {
        return this.AssertPullRequestReadFailsAsync(static () => Responses.Status(HttpStatusCode.InternalServerError));
    }

    [Fact]
    public Task APullRequestReadWithANullBodyIsNotHandedOffAsync()
    {
        return this.AssertPullRequestReadFailsAsync(static () => Json("null"));
    }

    [Fact]
    public Task APullRequestReadThatCannotReachGitHubIsNotHandedOffAsync()
    {
        return this.AssertPullRequestReadFailsAsync(static () => TransportFails());
    }

    private async Task AssertPullRequestReadFailsAsync(Func<HttpResponseMessage> response)
    {
        using RecordingHttpMessageHandler upstream = Flow(response);

        PrHandOffResult result = await this.HandOffAsync(upstream);

        Assert.Equal(expected: PrHandOffOutcome.NotHandedOff, actual: result.Outcome);
        Assert.Equal(
            expected: REPOSITORY + "#93 was not handed off: could not read the pull request",
            actual: result.Message
        );
        Assert.Equal(expected: [], actual: result.StepsDone);
        Assert.Single(upstream.Requests);
        await this.DidNotWriteTheBoardAsync();
    }

    [Fact]
    public async Task ARefusedTitleUpdateIsNotHandedOffWithNoStepsDoneAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => PullRequest(),
            static () => AllowsAutoMerge(),
            static () => BaseBranch(isProtected: true),
            static () => Responses.Status(HttpStatusCode.UnprocessableEntity)
        );

        PrHandOffResult result = await this.HandOffAsync(upstream);

        Assert.Equal(expected: PrHandOffOutcome.NotHandedOff, actual: result.Outcome);
        Assert.Equal(
            expected: REPOSITORY
                + "#93 was not handed off: step failed: title and description set (GitHub answered with status 422)",
            actual: result.Message
        );
        Assert.Equal(expected: [], actual: result.StepsDone);
        Assert.Equal(expected: 4, actual: upstream.Requests.Count);
        await this.DidNotWriteTheBoardAsync();
    }

    [Fact]
    public async Task AnUpdateOfAnotherPullRequestMayHaveBeenHandedOffAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => PullRequest(),
            static () => AllowsAutoMerge(),
            static () => BaseBranch(isProtected: true),
            static () => Json("{\"number\":94}")
        );

        PrHandOffResult result = await this.HandOffAsync(upstream);

        Assert.Equal(expected: PrHandOffOutcome.MayHaveBeenHandedOff, actual: result.Outcome);
        Assert.Equal(expected: [], actual: result.StepsDone);
        await this.DidNotWriteTheBoardAsync();
    }

    [Fact]
    public async Task ABoardWriteFailureStopsBeforeReadyAsync()
    {
        this.BoardWriteThrows(new BoardWriteException("the board write was refused"));
        using RecordingHttpMessageHandler upstream = Flow(
            static () => PullRequest(),
            static () => AllowsAutoMerge(),
            static () => BaseBranch(isProtected: true),
            static () => Updated()
        );

        PrHandOffResult result = await this.HandOffAsync(upstream);

        Assert.Equal(expected: PrHandOffOutcome.MayHaveBeenHandedOff, actual: result.Outcome);
        Assert.Contains(
            expectedSubstring: "the Workflow board did not confirm the change",
            actualString: result.Message,
            comparisonType: StringComparison.Ordinal
        );
        Assert.Equal(expected: ["title and description set"], actual: result.StepsDone);
        Assert.Equal(expected: 4, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task ABoardWriteRefusedByGitHubIsNotHandedOffAsync()
    {
        this.BoardWriteThrows(new BoardWriteException("refused", new GitHubGraphQlException("Not found")));
        using RecordingHttpMessageHandler upstream = Flow(
            static () => PullRequest(),
            static () => AllowsAutoMerge(),
            static () => BaseBranch(isProtected: true),
            static () => Updated()
        );

        PrHandOffResult result = await this.HandOffAsync(upstream);

        Assert.Equal(expected: PrHandOffOutcome.NotHandedOff, actual: result.Outcome);
        Assert.Equal(
            expected: REPOSITORY
                + "#93 was not handed off: step failed: Workflow board set to Human Review (GitHub refused the change)",
            actual: result.Message
        );
        Assert.Equal(expected: ["title and description set"], actual: result.StepsDone);
    }

    [Fact]
    public async Task AMarkReadyFailureStopsBeforeTheRecordAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => PullRequest(),
            static () => AllowsAutoMerge(),
            static () => BaseBranch(isProtected: true),
            static () => Updated(),
            static () => Json(GRAPHQL_ERROR)
        );

        PrHandOffResult result = await this.HandOffAsync(upstream);

        Assert.Equal(expected: PrHandOffOutcome.NotHandedOff, actual: result.Outcome);
        Assert.Equal(
            expected: ["title and description set", "Workflow board set to Human Review"],
            actual: result.StepsDone
        );
        Assert.Equal(expected: -1, actual: IndexOf(upstream: upstream, method: HttpMethod.Post, uri: CommentsUri));
        await this.ReceivedBoardWriteAsync();
    }

    [Theory]
    [InlineData("""{"data":{"markPullRequestReadyForReview":{"pullRequest":{"isDraft":true}}}}""")]
    [InlineData("""{"data":{"markPullRequestReadyForReview":{"pullRequest":null}}}""")]
    [InlineData("""{"data":{"markPullRequestReadyForReview":null}}""")]
    public async Task AnUnconfirmedMarkReadyMayHaveBeenHandedOffAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => PullRequest(),
            static () => AllowsAutoMerge(),
            static () => BaseBranch(isProtected: true),
            static () => Updated(),
            () => Json(json)
        );

        PrHandOffResult result = await this.HandOffAsync(upstream);

        Assert.Equal(expected: PrHandOffOutcome.MayHaveBeenHandedOff, actual: result.Outcome);
        Assert.Contains(
            expectedSubstring: "(the answer from GitHub could not be read)",
            actualString: result.Message,
            comparisonType: StringComparison.Ordinal
        );
        Assert.Equal(
            expected: ["title and description set", "Workflow board set to Human Review"],
            actual: result.StepsDone
        );
        Assert.Equal(expected: -1, actual: IndexOf(upstream: upstream, method: HttpMethod.Post, uri: CommentsUri));
    }

    [Fact]
    public async Task ARefusedRecordIsNotHandedOffAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => PullRequest(draft: false),
            static () => AllowsAutoMerge(),
            static () => BaseBranch(isProtected: true),
            static () => Updated(),
            static () => Responses.Status(HttpStatusCode.Forbidden)
        );

        PrHandOffResult result = await this.HandOffAsync(upstream);

        Assert.Equal(expected: PrHandOffOutcome.NotHandedOff, actual: result.Outcome);
        Assert.Equal(
            expected: REPOSITORY
                + "#93 was not handed off: step failed: review record posted (GitHub answered with status 403)",
            actual: result.Message
        );
        Assert.Equal(
            expected: ["title and description set", "Workflow board set to Human Review"],
            actual: result.StepsDone
        );
        Assert.Equal(expected: 5, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task ARecordThatCannotReachGitHubMayHaveBeenHandedOffAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => PullRequest(draft: false),
            static () => AllowsAutoMerge(),
            static () => BaseBranch(isProtected: true),
            static () => Updated(),
            static () => TransportFails()
        );

        PrHandOffResult result = await this.HandOffAsync(upstream);

        Assert.Equal(expected: PrHandOffOutcome.MayHaveBeenHandedOff, actual: result.Outcome);
        Assert.Equal(
            expected: "could not hand off "
                + REPOSITORY
                + "#93, whose last step may have been made (the request to GitHub failed); read the pull request before trying again",
            actual: result.Message
        );
        Assert.Equal(
            expected: ["title and description set", "Workflow board set to Human Review"],
            actual: result.StepsDone
        );
    }

    [Fact]
    public async Task ARecordWithoutAUrlMayHaveBeenHandedOffAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => PullRequest(draft: false),
            static () => AllowsAutoMerge(),
            static () => BaseBranch(isProtected: true),
            static () => Updated(),
            static () => Responses.StatusWithJson(statusCode: HttpStatusCode.Created, json: "{\"id\":555}")
        );

        PrHandOffResult result = await this.HandOffAsync(upstream);

        Assert.Equal(expected: PrHandOffOutcome.MayHaveBeenHandedOff, actual: result.Outcome);
        Assert.Contains(
            expectedSubstring: "(the answer from GitHub could not be read)",
            actualString: result.Message,
            comparisonType: StringComparison.Ordinal
        );
        Assert.Equal(
            expected: ["title and description set", "Workflow board set to Human Review"],
            actual: result.StepsDone
        );
    }

    [Fact]
    public async Task AnUnexpectedRecordUrlMayHaveBeenHandedOffAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => PullRequest(),
            static () => AllowsAutoMerge(),
            static () => BaseBranch(isProtected: true),
            static () => Updated(),
            static () => Json(READY),
            static () => Posted(url: "https://github.com/" + REPOSITORY + "/pull/94#issuecomment-555")
        );

        PrHandOffResult result = await this.HandOffAsync(upstream);

        Assert.Equal(expected: PrHandOffOutcome.MayHaveBeenHandedOff, actual: result.Outcome);
        Assert.Equal(
            expected: "could not hand off "
                + REPOSITORY
                + "#93, whose last step may have been made (the answer from GitHub could not be read); read the pull request before trying again",
            actual: result.Message
        );
        Assert.Equal(
            expected: ["title and description set", "Workflow board set to Human Review", "marked ready for review"],
            actual: result.StepsDone
        );
        Assert.Equal(expected: 6, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task AMovedHeadLeavesAutoMergeOffAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => PullRequest(),
            static () => AllowsAutoMerge(),
            static () => BaseBranch(isProtected: true),
            static () => Updated(),
            static () => Json(READY),
            static () => Posted(),
            static () => PullRequest(draft: false, sha: OTHER_SHA)
        );

        PrHandOffResult result = await this.HandOffAsync(upstream);

        Assert.Equal(expected: PrHandOffOutcome.NotHandedOff, actual: result.Outcome);
        Assert.Equal(
            expected: REPOSITORY
                + "#93 was not handed off: the head changed or could not be read again, so auto-merge was not turned on",
            actual: result.Message
        );
        Assert.Equal(
            expected:
            [
                "title and description set",
                "Workflow board set to Human Review",
                "marked ready for review",
                "review record posted",
            ],
            actual: result.StepsDone
        );
        Assert.Equal(expected: 7, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task AnUnreadableHeadLeavesAutoMergeOffAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => PullRequest(draft: false),
            static () => AllowsAutoMerge(),
            static () => BaseBranch(isProtected: true),
            static () => Updated(),
            static () => Posted(),
            static () => Responses.Status(HttpStatusCode.NotFound)
        );

        PrHandOffResult result = await this.HandOffAsync(upstream);

        Assert.Equal(expected: PrHandOffOutcome.NotHandedOff, actual: result.Outcome);
        Assert.Equal(
            expected: ["title and description set", "Workflow board set to Human Review", "review record posted"],
            actual: result.StepsDone
        );
        Assert.Equal(expected: 6, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task AnIncompleteHeadReadLeavesAutoMergeOffAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => PullRequest(draft: false),
            static () => AllowsAutoMerge(),
            static () => BaseBranch(isProtected: true),
            static () => Updated(),
            static () => Posted(),
            static () => Json("{\"state\":\"open\"}")
        );

        PrHandOffResult result = await this.HandOffAsync(upstream);

        Assert.Equal(expected: PrHandOffOutcome.NotHandedOff, actual: result.Outcome);
        Assert.Equal(
            expected: REPOSITORY
                + "#93 was not handed off: the head changed or could not be read again, so auto-merge was not turned on",
            actual: result.Message
        );
        Assert.Equal(expected: 6, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task ARefusedAutoMergeIsNotHandedOffWithEveryEarlierStepAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => PullRequest(draft: false),
            static () => AllowsAutoMerge(),
            static () => BaseBranch(isProtected: true),
            static () => Updated(),
            static () => Posted(),
            static () => PullRequest(draft: false),
            static () => Json(GRAPHQL_ERROR)
        );

        PrHandOffResult result = await this.HandOffAsync(upstream);

        Assert.Equal(expected: PrHandOffOutcome.NotHandedOff, actual: result.Outcome);
        Assert.Equal(
            expected: REPOSITORY
                + "#93 was not handed off: step failed: auto-merge turned on (GitHub refused the change)",
            actual: result.Message
        );
        Assert.Equal(
            expected: ["title and description set", "Workflow board set to Human Review", "review record posted"],
            actual: result.StepsDone
        );
    }

    [Theory]
    [InlineData("""{"data":{"enablePullRequestAutoMerge":{"pullRequest":{"autoMergeRequest":null}}}}""")]
    [InlineData("""{"data":{"enablePullRequestAutoMerge":{"pullRequest":null}}}""")]
    [InlineData("""{"data":{"enablePullRequestAutoMerge":null}}""")]
    public async Task AnUnconfirmedAutoMergeMayHaveBeenHandedOffAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => PullRequest(draft: false),
            static () => AllowsAutoMerge(),
            static () => BaseBranch(isProtected: true),
            static () => Updated(),
            static () => Posted(),
            static () => PullRequest(draft: false),
            () => Json(json)
        );

        PrHandOffResult result = await this.HandOffAsync(upstream);

        Assert.Equal(expected: PrHandOffOutcome.MayHaveBeenHandedOff, actual: result.Outcome);
        Assert.Equal(
            expected: ["title and description set", "Workflow board set to Human Review", "review record posted"],
            actual: result.StepsDone
        );
    }

    [Theory]
    [InlineData("credfeto")]
    [InlineData("credfeto/")]
    [InlineData("")]
    public async Task AnInvalidRepositoryIsAUsageErrorAsync(string repository)
    {
        using RecordingHttpMessageHandler upstream = Flow();

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.HandOffAsync(upstream: upstream, repository: repository)
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
            this.HandOffAsync(upstream: upstream, pullRequest: pullRequest)
        );

        Assert.Equal(expected: "pr", actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("feat: bad\u0007title")]
    public async Task AnInvalidTitleIsAUsageErrorAsync(string title)
    {
        using RecordingHttpMessageHandler upstream = Flow();

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.HandOffAsync(upstream: upstream, title: title)
        );

        Assert.Equal(expected: "title", actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \n\t")]
    public async Task AnEmptyDescriptionIsAUsageErrorAsync(string description)
    {
        using RecordingHttpMessageHandler upstream = Flow();

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.HandOffAsync(upstream: upstream, description: description)
        );

        Assert.Equal(expected: "description", actual: exception.ParameterName);
        Assert.Equal(expected: "the description is empty", actual: exception.Message);
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task AnEmptyRecordIsAUsageErrorAsync(string record)
    {
        using RecordingHttpMessageHandler upstream = Flow();

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.HandOffAsync(upstream: upstream, record: record)
        );

        Assert.Equal(expected: "record", actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task AFailedBoardResolveWritesNothingAsync()
    {
        this._boardResolver.ResolveAsync(repository: Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>())
            .ThrowsAsync(new WorkflowBoardNotFoundException("no board"));
        using RecordingHttpMessageHandler upstream = Flow();

        await Assert.ThrowsAsync<WorkflowBoardNotFoundException>(() => this.HandOffAsync(upstream));

        Assert.Empty(upstream.Requests);
        await this.DidNotWriteTheBoardAsync();
    }

    [Fact]
    public async Task OnlyTheLabelsWithNamesAreReadAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => PullRequest(labels: "null,{\"name\":null},{\"name\":\"enhancement\"}"),
            static () => AllowsAutoMerge(allowed: false),
            static () => Updated(),
            static () => Json(READY),
            static () => Posted()
        );

        PrHandOffResult result = await this.HandOffAsync(upstream);

        Assert.Equal(expected: PrHandOffOutcome.HandedOff, actual: result.Outcome);
        Assert.True(
            upstream
                .Requests.Select(request => request.Method)
                .SequenceEqual([HttpMethod.Get, HttpMethod.Get, HttpMethod.Patch, HttpMethod.Post, HttpMethod.Post]),
            userMessage: "reads, then the update, ready and record"
        );
    }
}
