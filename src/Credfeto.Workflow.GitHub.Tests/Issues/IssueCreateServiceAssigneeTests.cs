using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Issues;

public sealed class IssueCreateServiceAssigneeTests : IssueCreateServiceTestBase
{
    private const string AGENT = "steward-warden";

    private const string NOT_ASSIGNED_REASON = "GitHub leaves out an assignee who cannot be assigned in the repository";

    private static readonly Uri UserUri = new(GitHubServices.ApiBaseUrl + "user");

    private static HttpResponseMessage Existing()
    {
        return Responses.Json("""[{"name":"Medium"}]""");
    }

    private static HttpResponseMessage CreatedWith(string assignees)
    {
        return Responses.Json("{\"html_url\":\"" + ISSUE_URL + "\",\"assignees\":[" + assignees + "]}");
    }

    private static RecordingHttpMessageHandler Flow(params Func<HttpResponseMessage>[] responses)
    {
        return new(index => index < responses.Length ? responses[index]() : Unexpected(index));
    }

    private static void AssertIssueRequest(RecordedRequest request, string assignees)
    {
        Assert.Equal(expected: HttpMethod.Post, actual: request.Method);
        Assert.Equal(expected: IssuesUri, actual: request.RequestUri);
        Assert.Equal(
            expected: """{"title":"feat(mcp): add issue create","body":"## Summary\n\nCreate an issue.","labels":["Medium"],"assignees":"""
                + assignees
                + "}",
            actual: request.Body
        );
    }

    [Fact]
    public async Task AssigneesAreSentWithTheIssueAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(Existing, static () =>
            CreatedWith("""{"login":"octocat"},{"login":"credfeto"}""")
        );

        IssueCreateResponse response = await this.CreateAsync(upstream: upstream, assignees: ["octocat", "credfeto"]);

        Assert.Equal(expected: new Uri(ISSUE_URL), actual: response.Url);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
        AssertIssueRequest(request: upstream.Requests[1], assignees: """["octocat","credfeto"]""");
        await this.ReceivedBoardWriteAsync(owner: OWNER, repo: NAME, number: ISSUE);
    }

    [Fact]
    public async Task AnAssigneeRepeatedInAnotherCaseIsSentOnceAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(Existing, static () =>
            CreatedWith("""{"login":"octocat"}""")
        );

        await this.CreateAsync(upstream: upstream, assignees: ["octocat", "OctoCat"]);

        AssertIssueRequest(request: upstream.Requests[1], assignees: """["octocat"]""");
    }

    [Fact]
    public async Task AssignSelfReadsTheSignedInAccountAndSendsItAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => Responses.Json("{\"login\":\"" + AGENT + "\"}"),
            Existing,
            static () => CreatedWith("""{"login":"octocat"},{"login":"Steward-Warden"}""")
        );

        IssueCreateResponse response = await this.CreateAsync(
            upstream: upstream,
            assignees: ["octocat"],
            assignSelf: true
        );

        Assert.Equal(expected: new Uri(ISSUE_URL), actual: response.Url);
        Assert.Equal(expected: HttpMethod.Get, actual: upstream.Requests[0].Method);
        Assert.Equal(expected: UserUri, actual: upstream.Requests[0].RequestUri);
        AssertIssueRequest(request: upstream.Requests[2], assignees: """["octocat","steward-warden"]""");
    }

    [Fact]
    public async Task AssignSelfAlreadyNamedIsSentOnceAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(
            static () => Responses.Json("{\"login\":\"" + AGENT + "\"}"),
            Existing,
            static () => CreatedWith("""{"login":"steward-warden"}""")
        );

        await this.CreateAsync(upstream: upstream, assignees: ["Steward-Warden"], assignSelf: true);

        AssertIssueRequest(request: upstream.Requests[2], assignees: """["Steward-Warden"]""");
    }

    [Theory]
    [InlineData("""{"login":null}""")]
    [InlineData("""{"login":""}""")]
    [InlineData("not json")]
    public async Task AnUnreadableAccountCreatesNothingAsync(string user)
    {
        using RecordingHttpMessageHandler upstream = Flow(() => Responses.Json(user));

        IssueCreateFailedException exception = await Assert.ThrowsAsync<IssueCreateFailedException>(() =>
            this.CreateAsync(upstream: upstream, assignSelf: true)
        );

        Assert.Equal(expected: "could not read the signed-in account", actual: exception.Message);
        _ = Assert.Single(upstream.Requests);
        await this.DidNotWriteTheBoardAsync();
    }

    [Fact]
    public async Task AnAccountReadFailureCreatesNothingAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(static () => Responses.Status(HttpStatusCode.Unauthorized));

        IssueCreateFailedException exception = await Assert.ThrowsAsync<IssueCreateFailedException>(() =>
            this.CreateAsync(upstream: upstream, assignSelf: true)
        );

        Assert.Equal(expected: "could not read the signed-in account", actual: exception.Message);
        _ = Assert.Single(upstream.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a login")]
    [InlineData("octo/cat")]
    public async Task AnInvalidAssigneeIsRefusedWithoutCallingGitHubAsync(string assignee)
    {
        using RecordingHttpMessageHandler upstream = Flow();

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.CreateAsync(upstream: upstream, assignees: ["octocat", assignee])
        );

        Assert.Equal(expected: "assignees", actual: exception.ParameterName);
        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("""{"login":"credfeto"}""")]
    [InlineData("""null,{"login":null}""")]
    public async Task AnAssigneeGitHubLeftOutFailsWithTheUrlAfterPuttingTheIssueOnTheBoardAsync(string answered)
    {
        using RecordingHttpMessageHandler upstream = Flow(Existing, () => CreatedWith(answered));

        IssueCreateNotAssignedException exception = await Assert.ThrowsAsync<IssueCreateNotAssignedException>(() =>
            this.CreateAsync(upstream: upstream, assignees: ["octocat"])
        );

        Assert.Equal(
            expected: "octocat was not assigned; " + NOT_ASSIGNED_REASON + "; the issue was created: " + ISSUE_URL,
            actual: exception.Message
        );
        Assert.Equal(expected: new Uri(ISSUE_URL), actual: exception.Url);
        await this.ReceivedBoardWriteAsync(owner: OWNER, repo: NAME, number: ISSUE);
    }

    [Fact]
    public async Task AnAnswerWithoutAssigneesNamesEveryOneLeftOutAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(Existing, static () => CreatedIssue(ISSUE_URL));

        IssueCreateNotAssignedException exception = await Assert.ThrowsAsync<IssueCreateNotAssignedException>(() =>
            this.CreateAsync(upstream: upstream, assignees: ["octocat", "credfeto"])
        );

        Assert.Equal(
            expected: "octocat, credfeto were not assigned; "
                + NOT_ASSIGNED_REASON
                + "; the issue was created: "
                + ISSUE_URL,
            actual: exception.Message
        );
    }

    [Fact]
    public async Task ABoardFailureAndAnAssigneeLeftOutAreBothReportedAsync()
    {
        BoardWriteException failure = new("could not add " + ISSUE_URL + " to project 7");
        this.BoardWriter.SetItemStatusAsync(
                owner: OWNER,
                repo: NAME,
                kind: WorkItemKind.ISSUE,
                number: ISSUE,
                project: Project,
                fieldId: FIELD_ID,
                fieldName: FIELD_NAME,
                optionId: OPTION_ID,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);
        using RecordingHttpMessageHandler upstream = Flow(Existing, static () => CreatedWith(string.Empty));

        IssueCreateBoardWriteException exception = await Assert.ThrowsAsync<IssueCreateBoardWriteException>(() =>
            this.CreateAsync(upstream: upstream, assignees: ["octocat"])
        );

        Assert.Equal(
            expected: "could not add "
                + ISSUE_URL
                + " to project 7; the issue was already created: "
                + ISSUE_URL
                + "; octocat was not assigned; "
                + NOT_ASSIGNED_REASON,
            actual: exception.Message
        );
        Assert.Equal(expected: new Uri(ISSUE_URL), actual: exception.Url);
        IssueCreateBoardWriteException inner = Assert.IsType<IssueCreateBoardWriteException>(exception.InnerException);
        Assert.Same(expected: failure, actual: inner.InnerException);
    }

    [Fact]
    public async Task ABoardFailureWithEveryAssigneeTakenIsReportedAloneAsync()
    {
        BoardWriteException failure = new("could not add " + ISSUE_URL + " to project 7");
        this.BoardWriter.SetItemStatusAsync(
                owner: OWNER,
                repo: NAME,
                kind: WorkItemKind.ISSUE,
                number: ISSUE,
                project: Project,
                fieldId: FIELD_ID,
                fieldName: FIELD_NAME,
                optionId: OPTION_ID,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);
        using RecordingHttpMessageHandler upstream = Flow(Existing, static () =>
            CreatedWith("""{"login":"OCTOCAT"}""")
        );

        IssueCreateBoardWriteException exception = await Assert.ThrowsAsync<IssueCreateBoardWriteException>(() =>
            this.CreateAsync(upstream: upstream, assignees: ["octocat"])
        );

        Assert.Equal(
            expected: "could not add " + ISSUE_URL + " to project 7; the issue was already created: " + ISSUE_URL,
            actual: exception.Message
        );
        Assert.Same(expected: failure, actual: exception.InnerException);
    }
}
