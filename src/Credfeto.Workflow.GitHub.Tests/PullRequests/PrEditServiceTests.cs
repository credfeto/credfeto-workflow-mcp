using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.PullRequests;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Credfeto.Workflow.GitHub.Validation;
using NSubstitute;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.PullRequests;

public sealed class PrEditServiceTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const int PULL_REQUEST = 87;
    private const string OLD_TITLE = "feat: old title";
    private const string NEW_TITLE = "feat: new title";
    private const string OLD_BODY = "Old body";
    private const string NEW_BODY = "New body";
    private const string PULL_REQUEST_URL = "https://github.com/credfeto/credfeto-workflow-mcp/pull/87";

    private const string USER = """{"login":"steward-warden"}""";

    private const string EDITED =
        """{"number":87,"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/pull/87"}""";

    private static readonly Uri PullRequestUri = new(GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/pulls/87");

    private static readonly Uri IssueUri = new(GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/issues/87");

    private static readonly Uri UserUri = new(GitHubServices.ApiBaseUrl + "user");

    private readonly ILeakChecker _leakChecker;

    public PrEditServiceTests()
    {
        this._leakChecker = GetSubstitute<ILeakChecker>();
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

    private static HttpResponseMessage Throw(string message)
    {
        throw new HttpRequestException(message);
    }

    private static RecordingHttpMessageHandler Flow(params Func<HttpResponseMessage>[] responses)
    {
        return new(index => index < responses.Length ? responses[index]() : Unexpected(index));
    }

    // The pull request was opened by another account, which never stops it being edited
    private static HttpResponseMessage PullRequest(string assignees = "")
    {
        return Responses.Json(
            "{\"number\":87,\"html_url\":\""
                + PULL_REQUEST_URL
                + "\",\"title\":\""
                + OLD_TITLE
                + "\",\"body\":\""
                + OLD_BODY
                + "\",\"user\":{\"login\":\"someone-else\"},\"assignees\":["
                + assignees
                + "]}"
        );
    }

    private static HttpResponseMessage Edited()
    {
        return Responses.Json(EDITED);
    }

    private PrEditService CreateService(RecordingHttpMessageHandler upstream)
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        return new(restClient: rest, leakChecker: this._leakChecker);
    }

    private async Task<PrEditResult> EditAsync(
        RecordingHttpMessageHandler upstream,
        string repository = REPOSITORY,
        int pullRequest = PULL_REQUEST,
        string? title = null,
        string? body = null,
        bool assignSelf = false
    )
    {
        return await this.CreateService(upstream)
            .EditAsync(
                repository: repository,
                pullRequest: pullRequest,
                title: title,
                body: body,
                assignSelf: assignSelf,
                cancellationToken: this.CancellationToken()
            );
    }

    private static void AssertEdited(PrEditResult result)
    {
        Assert.Equal(expected: PrEditOutcome.Edited, actual: result.Outcome);
        Assert.Equal(expected: "credfeto/credfeto-workflow-mcp#87 was edited", actual: result.Message);
        Assert.Equal(expected: new Uri(PULL_REQUEST_URL), actual: result.Url);
        Assert.Null(result.Rule);
        Assert.Null(result.LineNumber);
    }

    private static void AssertFailed(PrEditResult result, PrEditOutcome outcome, string message)
    {
        Assert.Equal(expected: outcome, actual: result.Outcome);
        Assert.Contains(
            expectedSubstring: message,
            actualString: result.Message,
            comparisonType: StringComparison.Ordinal
        );
        Assert.Null(result.Url);
        Assert.Null(result.Rule);
        Assert.Null(result.LineNumber);
    }

    private static void AssertRefused(PrEditResult result, string subject, int? lineNumber)
    {
        Assert.Equal(expected: PrEditOutcome.Refused, actual: result.Outcome);
        Assert.Equal(expected: "token", actual: result.Rule);
        Assert.Equal(expected: lineNumber, actual: result.LineNumber);
        Assert.StartsWith(
            expectedStartString: subject + " refused: token",
            actualString: result.Message,
            comparisonType: StringComparison.Ordinal
        );
        Assert.Null(result.Url);
    }

    private static void AssertWrite(RecordingHttpMessageHandler upstream, int index, string body)
    {
        Assert.Equal(expected: index + 1, actual: upstream.Requests.Count);
        Assert.Equal(expected: HttpMethod.Patch, actual: upstream.Requests[index].Method);
        Assert.Equal(expected: IssueUri, actual: upstream.Requests[index].RequestUri);
        Assert.Equal(expected: body, actual: upstream.Requests[index].Body);
    }

    private static void AssertReadPullRequest(RecordingHttpMessageHandler upstream, int index)
    {
        Assert.Equal(expected: HttpMethod.Get, actual: upstream.Requests[index].Method);
        Assert.Equal(expected: PullRequestUri, actual: upstream.Requests[index].RequestUri);
    }

    [Fact]
    public async Task TitleOnlyIsSentInOneWriteAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(static () => PullRequest(), Edited);

        PrEditResult result = await this.EditAsync(upstream: upstream, title: NEW_TITLE);

        AssertEdited(result);
        AssertReadPullRequest(upstream: upstream, index: 0);
        AssertWrite(upstream: upstream, index: 1, body: """{"title":"feat: new title"}""");
    }

    [Fact]
    public async Task BodyOnlyIsSentInOneWriteAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(static () => PullRequest(), Edited);

        PrEditResult result = await this.EditAsync(upstream: upstream, body: NEW_BODY);

        AssertEdited(result);
        AssertReadPullRequest(upstream: upstream, index: 0);
        AssertWrite(upstream: upstream, index: 1, body: """{"body":"New body"}""");
    }

    [Fact]
    public async Task AssignSelfKeepsTheOtherAssigneesAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => Responses.Json(USER),
            static () => PullRequest("""{"login":"credfeto"}"""),
            Edited
        );

        PrEditResult result = await this.EditAsync(upstream: upstream, assignSelf: true);

        AssertEdited(result);
        Assert.Equal(expected: HttpMethod.Get, actual: upstream.Requests[0].Method);
        Assert.Equal(expected: UserUri, actual: upstream.Requests[0].RequestUri);
        AssertReadPullRequest(upstream: upstream, index: 1);
        AssertWrite(upstream: upstream, index: 2, body: """{"assignees":["credfeto","steward-warden"]}""");
    }

    [Fact]
    public async Task AssignSelfAlreadyAssignedInAnotherCaseIsNotAddedTwiceAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => Responses.Json(USER),
            static () => PullRequest("""{"login":"Steward-Warden"}"""),
            Edited
        );

        PrEditResult result = await this.EditAsync(upstream: upstream, title: NEW_TITLE, assignSelf: true);

        AssertEdited(result);
        AssertWrite(
            upstream: upstream,
            index: 2,
            body: """{"title":"feat: new title","assignees":["Steward-Warden"]}"""
        );
    }

    [Fact]
    public async Task EveryChangeIsSentInOneWriteAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => Responses.Json(USER),
            static () => PullRequest(),
            Edited
        );

        PrEditResult result = await this.EditAsync(
            upstream: upstream,
            title: NEW_TITLE,
            body: NEW_BODY,
            assignSelf: true
        );

        AssertEdited(result);
        AssertWrite(
            upstream: upstream,
            index: 2,
            body: """{"title":"feat: new title","body":"New body","assignees":["steward-warden"]}"""
        );
    }

    [Fact]
    public async Task AlreadyAsAskedIsUnchangedAndWritesNothingAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => Responses.Json(USER),
            static () => PullRequest("""{"login":"Steward-Warden"}""")
        );

        PrEditResult result = await this.EditAsync(
            upstream: upstream,
            title: OLD_TITLE,
            body: OLD_BODY,
            assignSelf: true
        );

        Assert.Equal(expected: PrEditOutcome.Unchanged, actual: result.Outcome);
        Assert.Equal(
            expected: "credfeto/credfeto-workflow-mcp#87 already is as asked, so nothing was written",
            actual: result.Message
        );
        Assert.Equal(expected: new Uri(PULL_REQUEST_URL), actual: result.Url);
        Assert.Null(result.Rule);
        Assert.Null(result.LineNumber);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task OnlyTheChangedPartNeedsToDifferAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(static () => PullRequest(), Edited);

        PrEditResult result = await this.EditAsync(upstream: upstream, title: OLD_TITLE, body: NEW_BODY);

        AssertEdited(result);
        AssertWrite(upstream: upstream, index: 1, body: """{"title":"feat: old title","body":"New body"}""");
    }

    [Theory]
    [InlineData("not-a-repository")]
    [InlineData("")]
    public async Task InvalidRepositoryIsRefusedWithoutCallingGitHubAsync(string repository)
    {
        using RecordingHttpMessageHandler upstream = Flow();

        await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.EditAsync(upstream: upstream, repository: repository, title: NEW_TITLE)
        );

        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task InvalidPullRequestIsRefusedWithoutCallingGitHubAsync(int pullRequest)
    {
        using RecordingHttpMessageHandler upstream = Flow();

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.EditAsync(upstream: upstream, pullRequest: pullRequest, title: NEW_TITLE)
        );

        Assert.Equal(expected: "pr", actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task NothingToEditIsRefusedWithoutCallingGitHubAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow();

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.EditAsync(upstream: upstream)
        );

        Assert.Equal(expected: PrEditService.NOTHING_TO_EDIT_MESSAGE, actual: exception.Message);
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankTitleIsRefusedWithoutCallingGitHubAsync(string title)
    {
        using RecordingHttpMessageHandler upstream = Flow();

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.EditAsync(upstream: upstream, title: title)
        );

        Assert.Equal(expected: TitleValidator.INVALID_TITLE_MESSAGE, actual: exception.Message);
        Assert.Equal(expected: "title", actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("Not a conventional title")]
    [InlineData("feat:no space")]
    public async Task NonConventionalTitleIsRefusedWithoutCallingGitHubAsync(string title)
    {
        using RecordingHttpMessageHandler upstream = Flow();

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.EditAsync(upstream: upstream, title: title)
        );

        Assert.Equal(expected: TitleValidator.INVALID_PULL_REQUEST_TITLE_MESSAGE, actual: exception.Message);
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("​")]
    public async Task EmptyBodyIsRefusedWithoutCallingGitHubAsync(string body)
    {
        using RecordingHttpMessageHandler upstream = Flow();

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.EditAsync(upstream: upstream, body: body)
        );

        Assert.Equal(expected: "body", actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task TitleThatLeaksIsRefusedWithoutCallingGitHubAsync()
    {
        this._leakChecker.CheckValue(subject: LeakSubject.TITLE, value: NEW_TITLE)
            .Returns(new LeakRefusal(Subject: LeakSubject.TITLE, Rule: LeakRule.TOKEN, LineNumber: null));
        using RecordingHttpMessageHandler upstream = Flow();

        PrEditResult result = await this.EditAsync(upstream: upstream, title: NEW_TITLE, assignSelf: true);

        AssertRefused(result: result, subject: "title", lineNumber: null);
        Assert.Equal(expected: "title refused: token", actual: result.Message);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task TitleThatNamesAnotherRepositoryIsRefusedWithoutCallingGitHubAsync()
    {
        this._leakChecker.CheckRepositoryReferencesAsync(
                repository: REPOSITORY,
                subject: LeakSubject.TITLE,
                text: NEW_TITLE,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(new LeakRefusal(Subject: LeakSubject.TITLE, Rule: LeakRule.TOKEN, LineNumber: 1));
        using RecordingHttpMessageHandler upstream = Flow();

        PrEditResult result = await this.EditAsync(upstream: upstream, title: NEW_TITLE, body: NEW_BODY);

        AssertRefused(result: result, subject: "title", lineNumber: null);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task BodyThatLeaksIsRefusedWithItsLineWithoutCallingGitHubAsync()
    {
        this._leakChecker.CheckBody(NEW_BODY)
            .Returns(new LeakRefusal(Subject: LeakSubject.BODY, Rule: LeakRule.TOKEN, LineNumber: 3));
        using RecordingHttpMessageHandler upstream = Flow();

        PrEditResult result = await this.EditAsync(upstream: upstream, title: NEW_TITLE, body: NEW_BODY);

        AssertRefused(result: result, subject: "body", lineNumber: 3);
        Assert.Equal(expected: "body refused: token (line 3)", actual: result.Message);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task BodyThatNamesAnotherRepositoryIsRefusedWithoutCallingGitHubAsync()
    {
        this._leakChecker.CheckRepositoryReferencesAsync(
                repository: REPOSITORY,
                subject: LeakSubject.BODY,
                text: NEW_BODY,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(new LeakRefusal(Subject: LeakSubject.BODY, Rule: LeakRule.TOKEN, LineNumber: 2));
        using RecordingHttpMessageHandler upstream = Flow();

        PrEditResult result = await this.EditAsync(upstream: upstream, body: NEW_BODY);

        AssertRefused(result: result, subject: "body", lineNumber: 2);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task MissingPullRequestIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(static () => Responses.Status(HttpStatusCode.NotFound));

        PrEditResult result = await this.EditAsync(upstream: upstream, title: NEW_TITLE);

        AssertFailed(
            result: result,
            outcome: PrEditOutcome.NotFound,
            message: "the pull request credfeto/credfeto-workflow-mcp#87 was not found, or the caller cannot see it"
        );
        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task PullRequestReadFailureIsNotEditedAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(static () =>
            Responses.Status(HttpStatusCode.UnprocessableEntity)
        );

        PrEditResult result = await this.EditAsync(upstream: upstream, title: NEW_TITLE);

        AssertFailed(
            result: result,
            outcome: PrEditOutcome.NotEdited,
            message: "credfeto/credfeto-workflow-mcp#87 was not edited: could not read the pull request"
        );
        Assert.Single(upstream.Requests);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData(
        """{"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/pull/87","title":"t","assignees":[]}"""
    )]
    [InlineData(
        """{"number":88,"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/pull/87","title":"t","assignees":[]}"""
    )]
    [InlineData(
        """{"number":87,"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/pull/87","assignees":[]}"""
    )]
    [InlineData("""{"number":87,"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/pull/87","title":"t"}""")]
    [InlineData(
        """{"number":87,"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/pull/87","title":"t","assignees":[null]}"""
    )]
    [InlineData(
        """{"number":87,"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/pull/87","title":"t","assignees":[{"login":""}]}"""
    )]
    [InlineData("""{"number":87,"title":"t","assignees":[]}""")]
    [InlineData(
        """{"number":87,"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/issues/87","title":"t","assignees":[]}"""
    )]
    [InlineData(
        """{"number":87,"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/pull/88","title":"t","assignees":[]}"""
    )]
    [InlineData(
        """{"number":87,"html_url":"http://github.com/credfeto/credfeto-workflow-mcp/pull/87","title":"t","assignees":[]}"""
    )]
    public async Task UnexpectedPullRequestIsNotEditedAsync(string pullRequest)
    {
        using RecordingHttpMessageHandler upstream = Flow(() => Responses.Json(pullRequest));

        PrEditResult result = await this.EditAsync(upstream: upstream, title: NEW_TITLE);

        AssertFailed(
            result: result,
            outcome: PrEditOutcome.NotEdited,
            message: "credfeto/credfeto-workflow-mcp#87 was not edited: could not read the pull request"
        );
        Assert.Single(upstream.Requests);
    }

    [Theory]
    [InlineData("""{"login":null}""")]
    [InlineData("""{"login":""}""")]
    [InlineData("not json")]
    public async Task UnreadableAccountIsNotEditedAsync(string user)
    {
        using RecordingHttpMessageHandler upstream = Flow(() => Responses.Json(user));

        PrEditResult result = await this.EditAsync(upstream: upstream, assignSelf: true);

        AssertFailed(
            result: result,
            outcome: PrEditOutcome.NotEdited,
            message: "credfeto/credfeto-workflow-mcp#87 was not edited: could not read the signed-in account"
        );
        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task AccountReadFailureIsNotEditedAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(static () => Responses.Status(HttpStatusCode.Unauthorized));

        PrEditResult result = await this.EditAsync(upstream: upstream, assignSelf: true);

        AssertFailed(result: result, outcome: PrEditOutcome.NotEdited, message: "could not read the signed-in account");
        Assert.Single(upstream.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task RejectedWriteIsNotEditedAsync(HttpStatusCode statusCode)
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => PullRequest(),
            () => Responses.Status(statusCode)
        );

        PrEditResult result = await this.EditAsync(upstream: upstream, title: NEW_TITLE);

        AssertFailed(
            result: result,
            outcome: PrEditOutcome.NotEdited,
            message: "credfeto/credfeto-workflow-mcp#87 was not edited: GitHub answered with status "
                + ((int)statusCode).ToString(CultureInfo.InvariantCulture)
        );
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotModified)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task FailedWriteMayHaveBeenEditedAndIsNotRetriedAsync(HttpStatusCode statusCode)
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => PullRequest(),
            () => Responses.Status(statusCode)
        );

        PrEditResult result = await this.EditAsync(upstream: upstream, title: NEW_TITLE);

        AssertFailed(
            result: result,
            outcome: PrEditOutcome.MayHaveBeenEdited,
            message: "credfeto/credfeto-workflow-mcp#87 may have been edited (GitHub answered with status "
                + ((int)statusCode).ToString(CultureInfo.InvariantCulture)
                + "); read the pull request before trying again"
        );
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task UnreadableWriteAnswerMayHaveBeenEditedAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => PullRequest(),
            static () => Responses.Json("not json")
        );

        PrEditResult result = await this.EditAsync(upstream: upstream, title: NEW_TITLE);

        AssertFailed(
            result: result,
            outcome: PrEditOutcome.MayHaveBeenEdited,
            message: "the answer from GitHub could not be read"
        );
    }

    [Fact]
    public async Task WriteAnswerForAnotherNumberMayHaveBeenEditedAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => PullRequest(),
            static () => Responses.Json("""{"number":88}""")
        );

        PrEditResult result = await this.EditAsync(upstream: upstream, title: NEW_TITLE);

        AssertFailed(result: result, outcome: PrEditOutcome.MayHaveBeenEdited, message: "unexpected answer");
    }

    [Fact]
    public async Task WriteRequestFailureMayHaveBeenEditedAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => PullRequest(),
            static () => Throw("connection reset with secret detail")
        );

        PrEditResult result = await this.EditAsync(upstream: upstream, title: NEW_TITLE);

        AssertFailed(result: result, outcome: PrEditOutcome.MayHaveBeenEdited, message: "the request to GitHub failed");
        Assert.DoesNotContain(
            expectedSubstring: "secret detail",
            actualString: result.Message,
            comparisonType: StringComparison.Ordinal
        );
    }
}
