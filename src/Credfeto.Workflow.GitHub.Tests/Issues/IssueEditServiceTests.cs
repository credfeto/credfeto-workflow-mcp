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
using Credfeto.Workflow.GitHub.Issues;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Credfeto.Workflow.GitHub.Validation;
using NSubstitute;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Issues;

public sealed class IssueEditServiceTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const int ISSUE = 47;
    private const string OLD_TITLE = "tool: old title";
    private const string NEW_TITLE = "tool(cfwf): new title";
    private const string OLD_BODY = "Old body";
    private const string NEW_BODY = "New body";
    private const string ISSUE_URL = "https://github.com/credfeto/credfeto-workflow-mcp/issues/47";

    private const string USER = """{"login":"steward-warden"}""";

    private const string EDITED =
        """{"number":47,"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/issues/47","assignees":[{"login":"steward-warden"}]}""";

    private const string ALREADY_EXISTS = """
        {"message":"Validation Failed","errors":[{"resource":"Label","code":"already_exists","field":"name"}]}
        """;

    private const string LOW_LABEL = """{"name":"Low","color":"cc8899","description":"Low Priority"}""";

    private static readonly Uri IssueUri = new(GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/issues/47");

    private static readonly Uri UserUri = new(GitHubServices.ApiBaseUrl + "user");

    private static readonly Uri LabelsUri = new(GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/labels");

    private readonly ILeakChecker _leakChecker;

    public IssueEditServiceTests()
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

    // The issue was opened by another account, which never stops it being edited
    private static HttpResponseMessage Issue(string assignees = "", string labels = "")
    {
        return Responses.Json(
            "{\"number\":47,\"html_url\":\""
                + ISSUE_URL
                + "\",\"title\":\""
                + OLD_TITLE
                + "\",\"body\":\""
                + OLD_BODY
                + "\",\"user\":{\"login\":\"someone-else\"},\"assignees\":["
                + assignees
                + "],\"labels\":["
                + labels
                + "]}"
        );
    }

    private static HttpResponseMessage Labelled(string labels)
    {
        return Issue(labels: labels);
    }

    private static HttpResponseMessage LabelCreated()
    {
        return Responses.StatusWithJson(statusCode: HttpStatusCode.Created, json: """{"name":"Low"}""");
    }

    private static HttpResponseMessage Edited()
    {
        return Responses.Json(EDITED);
    }

    private IssueEditService CreateService(RecordingHttpMessageHandler upstream)
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            options: GitHubOptionsWith(),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        return new(restClient: rest, leakChecker: this._leakChecker);
    }

    private async Task<IssueEditResult> EditAsync(
        RecordingHttpMessageHandler upstream,
        string repository = REPOSITORY,
        int issue = ISSUE,
        string? title = null,
        string? body = null,
        string? priority = null,
        bool assignSelf = false
    )
    {
        return await this.CreateService(upstream)
            .EditAsync(
                repository: repository,
                issue: issue,
                title: title,
                body: body,
                priority: priority,
                assignSelf: assignSelf,
                cancellationToken: this.CancellationToken()
            );
    }

    private static void AssertEdited(IssueEditResult result)
    {
        Assert.Equal(expected: IssueEditOutcome.Edited, actual: result.Outcome);
        Assert.Equal(expected: "credfeto/credfeto-workflow-mcp#47 was edited", actual: result.Message);
        Assert.Equal(expected: new Uri(ISSUE_URL), actual: result.Url);
        Assert.Null(result.Rule);
        Assert.Null(result.LineNumber);
    }

    private static void AssertFailed(IssueEditResult result, IssueEditOutcome outcome, string message)
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

    private static void AssertRefused(IssueEditResult result, string subject, int? lineNumber)
    {
        Assert.Equal(expected: IssueEditOutcome.Refused, actual: result.Outcome);
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

    private static void AssertReadIssue(RecordingHttpMessageHandler upstream, int index)
    {
        Assert.Equal(expected: HttpMethod.Get, actual: upstream.Requests[index].Method);
        Assert.Equal(expected: IssueUri, actual: upstream.Requests[index].RequestUri);
    }

    [Fact]
    public async Task TitleOnlyIsSentInOneWriteAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(static () => Issue(), Edited);

        IssueEditResult result = await this.EditAsync(upstream: upstream, title: NEW_TITLE);

        AssertEdited(result);
        AssertReadIssue(upstream: upstream, index: 0);
        AssertWrite(upstream: upstream, index: 1, body: """{"title":"tool(cfwf): new title"}""");
    }

    [Fact]
    public async Task BodyOnlyIsSentInOneWriteAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(static () => Issue(), Edited);

        IssueEditResult result = await this.EditAsync(upstream: upstream, body: NEW_BODY);

        AssertEdited(result);
        AssertReadIssue(upstream: upstream, index: 0);
        AssertWrite(upstream: upstream, index: 1, body: """{"body":"New body"}""");
    }

    [Fact]
    public async Task AssignSelfOnAnItemAssignedToAnotherUserIsRefusedAndWritesNothingAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => Responses.Json(USER),
            static () => Issue("""{"login":"credfeto"}""")
        );

        IssueEditResult result = await this.EditAsync(
            upstream: upstream,
            title: NEW_TITLE,
            body: NEW_BODY,
            assignSelf: true
        );

        Assert.Equal(expected: IssueEditOutcome.Refused, actual: result.Outcome);
        Assert.Equal(expected: "already assigned to another user", actual: result.Rule);
        Assert.Equal(expected: "assignSelf refused: already assigned to another user", actual: result.Message);
        Assert.Null(result.LineNumber);
        Assert.Null(result.Url);
        Assert.Equal(expected: HttpMethod.Get, actual: upstream.Requests[0].Method);
        Assert.Equal(expected: UserUri, actual: upstream.Requests[0].RequestUri);
        AssertReadIssue(upstream: upstream, index: 1);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task AssignSelfAlreadyHeldAlongsideAnotherUserKeepsBothAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => Responses.Json(USER),
            static () => Issue("""{"login":"credfeto"},{"login":"STEWARD-WARDEN"}"""),
            Edited
        );

        IssueEditResult result = await this.EditAsync(upstream: upstream, title: NEW_TITLE, assignSelf: true);

        AssertEdited(result);
        AssertWrite(
            upstream: upstream,
            index: 2,
            body: """{"title":"tool(cfwf): new title","assignees":["credfeto","STEWARD-WARDEN"]}"""
        );
    }

    [Fact]
    public async Task EditWithoutAssignSelfOnAnItemAssignedToAnotherUserIsSentAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(static () => Issue("""{"login":"credfeto"}"""), Edited);

        IssueEditResult result = await this.EditAsync(upstream: upstream, title: NEW_TITLE);

        AssertEdited(result);
        AssertWrite(upstream: upstream, index: 1, body: """{"title":"tool(cfwf): new title"}""");
    }

    [Fact]
    public async Task AssignSelfIsSentInTheSameWriteAsATitleChangeAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => Responses.Json(USER),
            static () => Issue(),
            Edited
        );

        IssueEditResult result = await this.EditAsync(upstream: upstream, title: NEW_TITLE, assignSelf: true);

        AssertEdited(result);
        AssertWrite(
            upstream: upstream,
            index: 2,
            body: """{"title":"tool(cfwf): new title","assignees":["steward-warden"]}"""
        );
    }

    [Fact]
    public async Task AssignSelfAlreadyAssignedInAnotherCaseIsNotAddedTwiceAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => Responses.Json(USER),
            static () => Issue("""{"login":"Steward-Warden"}"""),
            Edited
        );

        IssueEditResult result = await this.EditAsync(upstream: upstream, title: NEW_TITLE, assignSelf: true);

        AssertEdited(result);
        AssertWrite(
            upstream: upstream,
            index: 2,
            body: """{"title":"tool(cfwf): new title","assignees":["Steward-Warden"]}"""
        );
    }

    [Fact]
    public async Task EveryChangeIsSentInOneWriteAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => Responses.Json(USER),
            static () => Issue(),
            Edited
        );

        IssueEditResult result = await this.EditAsync(
            upstream: upstream,
            title: NEW_TITLE,
            body: NEW_BODY,
            assignSelf: true
        );

        AssertEdited(result);
        AssertWrite(
            upstream: upstream,
            index: 2,
            body: """{"title":"tool(cfwf): new title","body":"New body","assignees":["steward-warden"]}"""
        );
    }

    [Theory]
    [InlineData("""{"number":47,"assignees":[]}""")]
    [InlineData("""{"number":47,"assignees":[{"login":"credfeto"}]}""")]
    [InlineData("""{"number":47,"assignees":[null,{"login":null}]}""")]
    [InlineData("""{"number":47}""")]
    public async Task AssignSelfThatGitHubLeftOutFailsWithTheUrlAsync(string answer)
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => Responses.Json(USER),
            static () => Issue(),
            () => Responses.Json(answer)
        );

        IssueEditResult result = await this.EditAsync(upstream: upstream, title: NEW_TITLE, assignSelf: true);

        Assert.Equal(expected: IssueEditOutcome.Failed, actual: result.Outcome);
        Assert.Equal(
            expected: "credfeto/credfeto-workflow-mcp#47 was edited but steward-warden was not assigned; GitHub leaves "
                + "out an assignee who cannot be assigned in the repository",
            actual: result.Message
        );
        Assert.Equal(expected: new Uri(ISSUE_URL), actual: result.Url);
        Assert.Null(result.Rule);
        Assert.Null(result.LineNumber);
        AssertWrite(
            upstream: upstream,
            index: 2,
            body: """{"title":"tool(cfwf): new title","assignees":["steward-warden"]}"""
        );
    }

    [Fact]
    public async Task AssignSelfAnsweredInAnotherCaseIsEditedAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => Responses.Json(USER),
            static () => Issue(),
            static () => Responses.Json("""{"number":47,"assignees":[{"login":"Steward-Warden"}]}""")
        );

        IssueEditResult result = await this.EditAsync(upstream: upstream, title: NEW_TITLE, assignSelf: true);

        AssertEdited(result);
    }

    [Fact]
    public async Task EditWithoutAssignSelfDoesNotCheckTheAnsweredAssigneesAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => Issue(),
            static () => Responses.Json("""{"number":47}""")
        );

        IssueEditResult result = await this.EditAsync(upstream: upstream, title: NEW_TITLE);

        AssertEdited(result);
    }

    [Fact]
    public async Task AlreadyAsAskedIsUnchangedAndWritesNothingAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => Responses.Json(USER),
            static () => Issue("""{"login":"Steward-Warden"}""")
        );

        IssueEditResult result = await this.EditAsync(
            upstream: upstream,
            title: OLD_TITLE,
            body: OLD_BODY,
            assignSelf: true
        );

        Assert.Equal(expected: IssueEditOutcome.Unchanged, actual: result.Outcome);
        Assert.Equal(
            expected: "credfeto/credfeto-workflow-mcp#47 already is as asked, so nothing was written",
            actual: result.Message
        );
        Assert.Equal(expected: new Uri(ISSUE_URL), actual: result.Url);
        Assert.Null(result.Rule);
        Assert.Null(result.LineNumber);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task OnlyTheChangedPartNeedsToDifferAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(static () => Issue(), Edited);

        IssueEditResult result = await this.EditAsync(upstream: upstream, title: OLD_TITLE, body: NEW_BODY);

        AssertEdited(result);
        AssertWrite(upstream: upstream, index: 1, body: """{"title":"tool: old title","body":"New body"}""");
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
    public async Task InvalidIssueIsRefusedWithoutCallingGitHubAsync(int issue)
    {
        using RecordingHttpMessageHandler upstream = Flow();

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.EditAsync(upstream: upstream, issue: issue, title: NEW_TITLE)
        );

        Assert.Equal(expected: "issue", actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task NothingToEditIsRefusedWithoutCallingGitHubAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow();

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.EditAsync(upstream: upstream)
        );

        Assert.Equal(expected: IssueEditService.NOTHING_TO_EDIT_MESSAGE, actual: exception.Message);
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
    [InlineData("Not a prefixed title")]
    [InlineData("Tool: upper-case prefix")]
    [InlineData("tool:no space")]
    public async Task UnshapedTitleInAListedOwnersRepositoryIsRefusedWithoutCallingGitHubAsync(string title)
    {
        using RecordingHttpMessageHandler upstream = Flow();

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.EditAsync(upstream: upstream, title: title)
        );

        Assert.Equal(expected: TitleValidator.INVALID_ISSUE_TITLE_MESSAGE, actual: exception.Message);
        Assert.Equal(expected: "title", actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task UnshapedTitleInAnotherOwnersRepositoryIsAcceptedAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(static () => Responses.Status(HttpStatusCode.NotFound));

        IssueEditResult result = await this.EditAsync(
            upstream: upstream,
            repository: "someone/repo",
            title: "Not a prefixed title"
        );

        Assert.Equal(expected: IssueEditOutcome.NotFound, actual: result.Outcome);
        Assert.Single(upstream.Requests);
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

        IssueEditResult result = await this.EditAsync(upstream: upstream, title: NEW_TITLE, assignSelf: true);

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

        IssueEditResult result = await this.EditAsync(upstream: upstream, title: NEW_TITLE, body: NEW_BODY);

        AssertRefused(result: result, subject: "title", lineNumber: null);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task BodyThatLeaksIsRefusedWithItsLineWithoutCallingGitHubAsync()
    {
        this._leakChecker.CheckBody(NEW_BODY)
            .Returns(new LeakRefusal(Subject: LeakSubject.BODY, Rule: LeakRule.TOKEN, LineNumber: 3));
        using RecordingHttpMessageHandler upstream = Flow();

        IssueEditResult result = await this.EditAsync(upstream: upstream, title: NEW_TITLE, body: NEW_BODY);

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

        IssueEditResult result = await this.EditAsync(upstream: upstream, body: NEW_BODY);

        AssertRefused(result: result, subject: "body", lineNumber: 2);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task MissingIssueIsNotFoundAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(static () => Responses.Status(HttpStatusCode.NotFound));

        IssueEditResult result = await this.EditAsync(upstream: upstream, title: NEW_TITLE);

        AssertFailed(
            result: result,
            outcome: IssueEditOutcome.NotFound,
            message: "the issue credfeto/credfeto-workflow-mcp#47 was not found, or the caller cannot see it"
        );
        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task PullRequestNumberIsNotFoundAndWritesNothingAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(static () =>
            Responses.Json(
                """{"number":47,"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/pull/47","title":"feat: t","assignees":[],"pull_request":{"url":"https://api.github.com/repos/credfeto/credfeto-workflow-mcp/pulls/47"}}"""
            )
        );

        IssueEditResult result = await this.EditAsync(upstream: upstream, title: NEW_TITLE);

        AssertFailed(
            result: result,
            outcome: IssueEditOutcome.NotFound,
            message: "the issue credfeto/credfeto-workflow-mcp#47 was not found, it is a pull request"
        );
        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task IssueReadFailureIsNotEditedAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(static () =>
            Responses.Status(HttpStatusCode.UnprocessableEntity)
        );

        IssueEditResult result = await this.EditAsync(upstream: upstream, title: NEW_TITLE);

        AssertFailed(
            result: result,
            outcome: IssueEditOutcome.Failed,
            message: "credfeto/credfeto-workflow-mcp#47 was not edited: could not read the issue"
        );
        Assert.Single(upstream.Requests);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData(
        """{"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/issues/47","title":"t","assignees":[]}"""
    )]
    [InlineData(
        """{"number":48,"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/issues/47","title":"t","assignees":[]}"""
    )]
    [InlineData(
        """{"number":47,"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/issues/47","assignees":[]}"""
    )]
    [InlineData(
        """{"number":47,"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/issues/47","title":"t"}"""
    )]
    [InlineData(
        """{"number":47,"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/issues/47","title":"t","assignees":[null]}"""
    )]
    [InlineData(
        """{"number":47,"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/issues/47","title":"t","assignees":[{"login":""}]}"""
    )]
    [InlineData("""{"number":47,"title":"t","assignees":[]}""")]
    [InlineData(
        """{"number":47,"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/pull/47","title":"t","assignees":[]}"""
    )]
    [InlineData(
        """{"number":47,"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/issues/48","title":"t","assignees":[]}"""
    )]
    [InlineData(
        """{"number":47,"html_url":"http://github.com/credfeto/credfeto-workflow-mcp/issues/47","title":"t","assignees":[]}"""
    )]
    public async Task UnexpectedIssueIsNotEditedAsync(string issue)
    {
        using RecordingHttpMessageHandler upstream = Flow(() => Responses.Json(issue));

        IssueEditResult result = await this.EditAsync(upstream: upstream, title: NEW_TITLE);

        AssertFailed(
            result: result,
            outcome: IssueEditOutcome.Failed,
            message: "credfeto/credfeto-workflow-mcp#47 was not edited: could not read the issue"
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

        IssueEditResult result = await this.EditAsync(upstream: upstream, assignSelf: true);

        AssertFailed(
            result: result,
            outcome: IssueEditOutcome.Failed,
            message: "credfeto/credfeto-workflow-mcp#47 was not edited: could not read the signed-in account"
        );
        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task AccountReadFailureIsNotEditedAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(static () => Responses.Status(HttpStatusCode.Unauthorized));

        IssueEditResult result = await this.EditAsync(upstream: upstream, assignSelf: true);

        AssertFailed(result: result, outcome: IssueEditOutcome.Failed, message: "could not read the signed-in account");
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
        using RecordingHttpMessageHandler upstream = Flow(static () => Issue(), () => Responses.Status(statusCode));

        IssueEditResult result = await this.EditAsync(upstream: upstream, title: NEW_TITLE);

        AssertFailed(
            result: result,
            outcome: IssueEditOutcome.Failed,
            message: "credfeto/credfeto-workflow-mcp#47 was not edited: GitHub answered with status "
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
        using RecordingHttpMessageHandler upstream = Flow(static () => Issue(), () => Responses.Status(statusCode));

        IssueEditResult result = await this.EditAsync(upstream: upstream, title: NEW_TITLE);

        AssertFailed(
            result: result,
            outcome: IssueEditOutcome.Failed,
            message: "credfeto/credfeto-workflow-mcp#47 may have been edited (GitHub answered with status "
                + ((int)statusCode).ToString(CultureInfo.InvariantCulture)
                + "); read the issue before trying again"
        );
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task UnreadableWriteAnswerMayHaveBeenEditedAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => Issue(),
            static () => Responses.Json("not json")
        );

        IssueEditResult result = await this.EditAsync(upstream: upstream, title: NEW_TITLE);

        AssertFailed(
            result: result,
            outcome: IssueEditOutcome.Failed,
            message: "may have been edited (the answer from GitHub could not be read)"
        );
    }

    [Fact]
    public async Task WriteAnswerForAnotherNumberMayHaveBeenEditedAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => Issue(),
            static () => Responses.Json("""{"number":48}""")
        );

        IssueEditResult result = await this.EditAsync(upstream: upstream, title: NEW_TITLE);

        AssertFailed(
            result: result,
            outcome: IssueEditOutcome.Failed,
            message: "credfeto/credfeto-workflow-mcp#47 may have been edited (unexpected answer)"
        );
    }

    [Fact]
    public async Task WriteRequestFailureMayHaveBeenEditedAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => Issue(),
            static () => Throw("connection reset with secret detail")
        );

        IssueEditResult result = await this.EditAsync(upstream: upstream, title: NEW_TITLE);

        AssertFailed(
            result: result,
            outcome: IssueEditOutcome.Failed,
            message: "may have been edited (the request to GitHub failed)"
        );
        Assert.DoesNotContain(
            expectedSubstring: "secret detail",
            actualString: result.Message,
            comparisonType: StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task PriorityReplacesTheOldOneInOneWriteAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => Labelled("""{"name":"bug"},{"name":"Medium"}"""),
            static () => Responses.Json("""[{"name":"bug"},{"name":"Medium"},{"name":"Low"}]"""),
            Edited
        );

        IssueEditResult result = await this.EditAsync(upstream: upstream, priority: "Low");

        AssertEdited(result);
        Assert.Equal(expected: "Medium", actual: result.OldPriority);
        Assert.Equal(expected: "Low", actual: result.NewPriority);
        AssertReadIssue(upstream: upstream, index: 0);
        AssertWrite(upstream: upstream, index: 2, body: """{"labels":["bug","Low"]}""");
    }

    [Fact]
    public async Task EveryOtherPriorityIsRemovedAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => Labelled("""{"name":"High"},{"name":"bug"},{"name":"low"}"""),
            Edited
        );

        IssueEditResult result = await this.EditAsync(upstream: upstream, priority: "Low");

        AssertEdited(result);
        Assert.Equal(expected: "High, low", actual: result.OldPriority);
        Assert.Equal(expected: "Low", actual: result.NewPriority);
        AssertWrite(upstream: upstream, index: 1, body: """{"labels":["bug","Low"]}""");
    }

    [Theory]
    [InlineData("lOw")]
    [InlineData("LOW")]
    public async Task PriorityIsWrittenInItsCanonicalSpellingAsync(string priority)
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => Labelled("""{"name":"Medium"},{"name":"low"}"""),
            Edited
        );

        IssueEditResult result = await this.EditAsync(upstream: upstream, priority: priority);

        Assert.Equal(expected: "Low", actual: result.NewPriority);
        AssertWrite(upstream: upstream, index: 1, body: """{"labels":["Low"]}""");
    }

    [Fact]
    public async Task PriorityMissingFromTheRepositoryIsCreatedWithItsStandardColourFirstAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => Labelled("""{"name":"bug"}"""),
            static () => Responses.Json("""[{"name":"bug"},{"name":"Medium"}]"""),
            LabelCreated,
            Edited
        );

        IssueEditResult result = await this.EditAsync(upstream: upstream, priority: "Low");

        AssertEdited(result);
        Assert.Null(result.OldPriority);
        Assert.Equal(expected: "Low", actual: result.NewPriority);
        Assert.Equal(expected: HttpMethod.Get, actual: upstream.Requests[1].Method);
        Assert.Equal(expected: new Uri(LabelsUri + "?per_page=100"), actual: upstream.Requests[1].RequestUri);
        Assert.Equal(expected: HttpMethod.Post, actual: upstream.Requests[2].Method);
        Assert.Equal(expected: LabelsUri, actual: upstream.Requests[2].RequestUri);
        Assert.Equal(expected: LOW_LABEL, actual: upstream.Requests[2].Body);
        AssertWrite(upstream: upstream, index: 3, body: """{"labels":["bug","Low"]}""");
    }

    [Fact]
    public async Task PriorityAlreadyInTheRepositoryIsNotCreatedAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => Labelled(""),
            static () => Responses.Json("""[{"name":"LOW"}]"""),
            Edited
        );

        IssueEditResult result = await this.EditAsync(upstream: upstream, priority: "Low");

        AssertEdited(result);
        AssertWrite(upstream: upstream, index: 2, body: """{"labels":["Low"]}""");
    }

    [Fact]
    public async Task PriorityLabelCreatedByAnotherCallerMeanwhileIsNotAnErrorAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => Labelled(""),
            static () => Responses.Json("[]"),
            static () => Responses.StatusWithJson(statusCode: HttpStatusCode.UnprocessableEntity, json: ALREADY_EXISTS),
            Edited
        );

        IssueEditResult result = await this.EditAsync(upstream: upstream, priority: "Low");

        AssertEdited(result);
        AssertWrite(upstream: upstream, index: 3, body: """{"labels":["Low"]}""");
    }

    [Theory]
    [InlineData(
        HttpStatusCode.UnprocessableEntity,
        """{"message":"Validation Failed","errors":[{"code":"invalid"}]}"""
    )]
    [InlineData(HttpStatusCode.InternalServerError, ALREADY_EXISTS)]
    [InlineData(HttpStatusCode.Forbidden, "{}")]
    public async Task PriorityLabelThatCannotBeCreatedWritesNothingAsync(HttpStatusCode status, string json)
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => Labelled(""),
            static () => Responses.Json("[]"),
            () => Responses.StatusWithJson(statusCode: status, json: json)
        );

        IssueEditResult result = await this.EditAsync(upstream: upstream, priority: "Low");

        AssertFailed(
            result: result,
            outcome: IssueEditOutcome.Failed,
            message: "credfeto/credfeto-workflow-mcp#47 was not edited: could not create the label 'Low' in "
                + REPOSITORY
        );
        Assert.Equal(expected: 3, actual: upstream.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "[]")]
    [InlineData(HttpStatusCode.OK, """[{"name":null}]""")]
    [InlineData(HttpStatusCode.OK, "[null]")]
    [InlineData(HttpStatusCode.OK, "not json")]
    public async Task RepositoryLabelsThatCannotBeReadWriteNothingAsync(HttpStatusCode status, string json)
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => Labelled(""),
            () => Responses.StatusWithJson(statusCode: status, json: json)
        );

        IssueEditResult result = await this.EditAsync(upstream: upstream, priority: "Low");

        AssertFailed(
            result: result,
            outcome: IssueEditOutcome.Failed,
            message: "credfeto/credfeto-workflow-mcp#47 was not edited: could not read the labels of " + REPOSITORY
        );
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task PriorityAlreadyTheOnlyOneIsUnchangedAndWritesNothingAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(static () => Labelled("""{"name":"bug"},{"name":"low"}"""));

        IssueEditResult result = await this.EditAsync(upstream: upstream, title: OLD_TITLE, priority: "Low");

        Assert.Equal(expected: IssueEditOutcome.Unchanged, actual: result.Outcome);
        Assert.Null(result.OldPriority);
        Assert.Null(result.NewPriority);
        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task PriorityAlreadySetIsNotRewrittenWhenTheTitleChangesAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(static () => Labelled("""{"name":"Low"}"""), Edited);

        IssueEditResult result = await this.EditAsync(upstream: upstream, title: NEW_TITLE, priority: "Low");

        AssertEdited(result);
        Assert.Null(result.OldPriority);
        Assert.Null(result.NewPriority);
        AssertWrite(upstream: upstream, index: 1, body: """{"title":"tool(cfwf): new title"}""");
    }

    [Fact]
    public async Task PriorityAndTitleAreSentInOneWriteAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => Labelled("""{"name":"Medium"}"""),
            static () => Responses.Json("""[{"name":"High"}]"""),
            Edited
        );

        IssueEditResult result = await this.EditAsync(upstream: upstream, title: NEW_TITLE, priority: "High");

        AssertEdited(result);
        AssertWrite(upstream: upstream, index: 2, body: """{"title":"tool(cfwf): new title","labels":["High"]}""");
    }

    [Fact]
    public async Task PriorityAndAssignSelfAreSentInOneWriteAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => Responses.Json(USER),
            static () => Labelled("""{"name":"Medium"},{"name":"High"}"""),
            Edited
        );

        IssueEditResult result = await this.EditAsync(upstream: upstream, priority: "High", assignSelf: true);

        AssertEdited(result);
        AssertWrite(upstream: upstream, index: 2, body: """{"assignees":["steward-warden"],"labels":["High"]}""");
    }

    [Theory]
    [InlineData("Low")]
    [InlineData("Urgent")]
    public async Task PriorityOtherThanSecurityOnASecurityItemIsRefusedAndWritesNothingAsync(string priority)
    {
        using RecordingHttpMessageHandler upstream = Flow(static () => Labelled("""{"name":"security"}"""));

        IssueEditResult result = await this.EditAsync(upstream: upstream, title: NEW_TITLE, priority: priority);

        Assert.Equal(expected: IssueEditOutcome.Refused, actual: result.Outcome);
        Assert.Equal(expected: "Security is never replaced", actual: result.Rule);
        Assert.Equal(expected: "priority refused: Security is never replaced", actual: result.Message);
        Assert.Null(result.LineNumber);
        Assert.Null(result.Url);
        Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task SecurityOnAnItemWithAnotherPriorityKeepsOnlySecurityAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => Labelled("""{"name":"Security"},{"name":"Low"}"""),
            Edited
        );

        IssueEditResult result = await this.EditAsync(upstream: upstream, priority: "security");

        AssertEdited(result);
        Assert.Equal(expected: "Security, Low", actual: result.OldPriority);
        Assert.Equal(expected: "Security", actual: result.NewPriority);
        AssertWrite(upstream: upstream, index: 1, body: """{"labels":["Security"]}""");
    }

    [Theory]
    [InlineData("Critical")]
    [InlineData("")]
    [InlineData("Low ")]
    public async Task UnknownPriorityIsRefusedWithoutCallingGitHubAsync(string priority)
    {
        using RecordingHttpMessageHandler upstream = Flow();

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.EditAsync(upstream: upstream, priority: priority)
        );

        Assert.Equal(expected: "priority", actual: exception.ParameterName);
        Assert.Equal(
            expected: "unknown priority (expected Security, Urgent, High, Medium or Low)",
            actual: exception.Message
        );
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData(
        """{"number":47,"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/issues/47","title":"t","assignees":[]}"""
    )]
    [InlineData(
        """{"number":47,"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/issues/47","title":"t","assignees":[],"labels":[null]}"""
    )]
    [InlineData(
        """{"number":47,"html_url":"https://github.com/credfeto/credfeto-workflow-mcp/issues/47","title":"t","assignees":[],"labels":[{"name":""}]}"""
    )]
    public async Task UnreadableLabelsWithAPriorityAreNotEditedAsync(string issue)
    {
        using RecordingHttpMessageHandler upstream = Flow(() => Responses.Json(issue));

        IssueEditResult result = await this.EditAsync(upstream: upstream, priority: "Low");

        AssertFailed(
            result: result,
            outcome: IssueEditOutcome.Failed,
            message: "credfeto/credfeto-workflow-mcp#47 was not edited: could not read the issue"
        );
        Assert.Single(upstream.Requests);
    }
}
