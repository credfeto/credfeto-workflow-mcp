using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Issues;

public sealed class IssueCreateServiceTests : IssueCreateServiceTestBase
{
    private const string ALREADY_EXISTS = """
        {"message":"Validation Failed","errors":[{"resource":"Label","code":"already_exists","field":"name"}]}
        """;

    [Fact]
    public async Task AnIssueIsCreatedWithThePriorityFirstAndPutOnTheBoardAsNotStartedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(ExistingLabelsFlow);

        IssueCreateResponse response = await this.CreateAsync(upstream: upstream, labels: ["bug", "custom"]);

        Assert.Equal(expected: new IssueCreateResponse(new Uri(ISSUE_URL)), actual: response);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
        Assert.Equal(expected: HttpMethod.Get, actual: upstream.Requests[0].Method);
        Assert.Equal(expected: new Uri(LabelsUri + "?per_page=100"), actual: upstream.Requests[0].RequestUri);
        Assert.Equal(expected: HttpMethod.Post, actual: upstream.Requests[1].Method);
        Assert.Equal(expected: IssuesUri, actual: upstream.Requests[1].RequestUri);
        AssertIssueRequest(request: upstream.Requests[1], labels: """["Medium","Bug","custom"]""");

        await this
            .StatusResolver.Received(1)
            .ResolveAsync(
                repository: REPOSITORY,
                project: Project,
                state: WorkflowStates.NOT_STARTED,
                cancellationToken: Arg.Any<CancellationToken>()
            );
        await this.ReceivedBoardWriteAsync(owner: OWNER, repo: NAME, number: ISSUE);
    }

    [Theory]
    [InlineData("urgent")]
    [InlineData("URGENT")]
    [InlineData("Urgent")]
    public async Task ThePriorityIsAppliedInItsCanonicalSpellingAsync(string priority)
    {
        using RecordingHttpMessageHandler upstream = new(ExistingLabelsFlow);

        await this.CreateAsync(upstream: upstream, priority: priority);

        AssertIssueRequest(request: upstream.Requests[1], labels: """["Urgent"]""");
    }

    [Fact]
    public async Task RepeatedLabelsAreDroppedWhateverTheirCaseAsync()
    {
        using RecordingHttpMessageHandler upstream = new(ExistingLabelsFlow);

        await this.CreateAsync(upstream: upstream, labels: ["Bug", "BUG", "custom", "Custom", "bug"]);

        AssertIssueRequest(request: upstream.Requests[1], labels: """["Medium","Bug","custom"]""");
    }

    [Fact]
    public async Task MissingLabelsAreCreatedWithTheStandardColourAndDescriptionAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index switch
            {
                0 => Responses.Json("[]"),
                1 or 2 or 3 => Responses.StatusWithJson(statusCode: HttpStatusCode.Created, json: """{"name":"x"}"""),
                4 => CreatedIssue(ISSUE_URL),
                _ => Unexpected(index),
            }
        );

        await this.CreateAsync(upstream: upstream, labels: ["on hold", "new-label"]);

        Assert.Equal(expected: 5, actual: upstream.Requests.Count);
        AssertLabelRequest(
            request: upstream.Requests[1],
            json: """{"name":"Medium","color":"ffff00","description":"Medium Priority"}"""
        );
        AssertLabelRequest(
            request: upstream.Requests[2],
            json: """{"name":"On Hold","color":"ff0000","description":"Do not work on this"}"""
        );
        AssertLabelRequest(request: upstream.Requests[3], json: """{"name":"new-label"}""");
        AssertIssueRequest(request: upstream.Requests[4], labels: """["Medium","On Hold","new-label"]""");
    }

    [Fact]
    public async Task ALabelCreatedByAnotherCallerMeanwhileIsNotAnErrorAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static index =>
            index switch
            {
                0 => Responses.Json("""[{"name":"Medium"}]"""),
                1 => Responses.StatusWithJson(statusCode: HttpStatusCode.UnprocessableEntity, json: ALREADY_EXISTS),
                2 => CreatedIssue(ISSUE_URL),
                _ => Unexpected(index),
            }
        );

        IssueCreateResponse response = await this.CreateAsync(upstream: upstream, labels: ["new-label"]);

        Assert.Equal(expected: new Uri(ISSUE_URL), actual: response.Url);
        AssertIssueRequest(request: upstream.Requests[2], labels: """["Medium","new-label"]""");
        await this.ReceivedBoardWriteAsync(owner: OWNER, repo: NAME, number: ISSUE);
    }

    [Theory]
    [InlineData(
        HttpStatusCode.UnprocessableEntity,
        """{"message":"Validation Failed","errors":[{"code":"invalid"}]}"""
    )]
    [InlineData(HttpStatusCode.UnprocessableEntity, """{"message":"Validation Failed","errors":["already_exists"]}""")]
    [InlineData(HttpStatusCode.UnprocessableEntity, """{"message":"Validation Failed","errors":[{"code":1}]}""")]
    [InlineData(HttpStatusCode.UnprocessableEntity, """{"message":"Validation Failed","errors":[{"field":"name"}]}""")]
    [InlineData(HttpStatusCode.UnprocessableEntity, """{"errors":{"code":"already_exists"}}""")]
    [InlineData(HttpStatusCode.UnprocessableEntity, """{"message":"Validation Failed"}""")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "not json")]
    [InlineData(HttpStatusCode.InternalServerError, ALREADY_EXISTS)]
    [InlineData(HttpStatusCode.Created, "null")]
    public async Task AnyOtherLabelCreateFailureStopsBeforeTheIssueIsCreatedAsync(HttpStatusCode status, string json)
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Responses.Json("""[{"name":"Medium"}]"""),
                1 => Responses.StatusWithJson(statusCode: status, json: json),
                _ => Unexpected(index),
            }
        );

        IssueCreateFailedException exception = await Assert.ThrowsAsync<IssueCreateFailedException>(() =>
            this.CreateAsync(upstream: upstream, labels: ["new-label"])
        );

        Assert.Equal(
            expected: "could not create the label 'new-label' in " + REPOSITORY,
            actual: exception.Message
        );
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
        await this.DidNotWriteTheBoardAsync();
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "[]")]
    [InlineData(HttpStatusCode.NotFound, "[]")]
    [InlineData(HttpStatusCode.OK, """[{"name":null}]""")]
    [InlineData(HttpStatusCode.OK, "[null]")]
    public async Task ALabelListingFailureStopsBeforeAnythingIsWrittenAsync(HttpStatusCode status, string json)
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0 ? Responses.StatusWithJson(statusCode: status, json: json) : Unexpected(index)
        );

        IssueCreateFailedException exception = await Assert.ThrowsAsync<IssueCreateFailedException>(() =>
            this.CreateAsync(upstream: upstream)
        );

        Assert.Equal(expected: "could not read the labels of " + REPOSITORY, actual: exception.Message);
        Assert.Single(upstream.Requests);
        await this.DidNotWriteTheBoardAsync();
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "{}")]
    [InlineData(HttpStatusCode.NotFound, "{}")]
    [InlineData(HttpStatusCode.UnprocessableEntity, """{"message":"Validation Failed"}""")]
    [InlineData(HttpStatusCode.Created, "null")]
    public async Task AnIssueCreateFailureIsReportedAsync(HttpStatusCode status, string json)
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Responses.Json("""[{"name":"Medium"}]"""),
                1 => Responses.StatusWithJson(statusCode: status, json: json),
                _ => Unexpected(index),
            }
        );

        IssueCreateFailedException exception = await Assert.ThrowsAsync<IssueCreateFailedException>(() =>
            this.CreateAsync(upstream: upstream)
        );

        Assert.Equal(expected: "could not create the issue in " + REPOSITORY, actual: exception.Message);
        await this.DidNotWriteTheBoardAsync();
    }

    [Theory]
    [InlineData("""{"html_url":null}""", "")]
    [InlineData("{}", "")]
    [InlineData(
        """{"html_url":"http://github.com/credfeto/repo/issues/1"}""",
        "http://github.com/credfeto/repo/issues/1"
    )]
    [InlineData(
        """{"html_url":"https://github.com/credfeto/repo/pull/1"}""",
        "https://github.com/credfeto/repo/pull/1"
    )]
    [InlineData(
        """{"html_url":"https://github.com/credfeto/repo/issues/0"}""",
        "https://github.com/credfeto/repo/issues/0"
    )]
    [InlineData(
        """{"html_url":"https://github.com/credfeto/repo/issues/99999999999"}""",
        "https://github.com/credfeto/repo/issues/99999999999"
    )]
    [InlineData(
        """{"html_url":"https://github.com/credfeto/repo/issues/1/comments"}""",
        "https://github.com/credfeto/repo/issues/1/comments"
    )]
    [InlineData(
        """{"html_url":"https://github.com/credfeto/repo/issues/1\nnext"}""",
        "https://github.com/credfeto/repo/issues/1 next"
    )]
    [InlineData(
        """{"html_url":"https://github.com/credfeto/repo/issues/1\n"}""",
        "https://github.com/credfeto/repo/issues/1 "
    )]
    public async Task AnUnexpectedIssueUrlIsReportedAsMaybeCreatedAsync(string json, string reported)
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Responses.Json("""[{"name":"Medium"}]"""),
                1 => Responses.StatusWithJson(statusCode: HttpStatusCode.Created, json: json),
                _ => Unexpected(index),
            }
        );

        IssueCreateFailedException exception = await Assert.ThrowsAsync<IssueCreateFailedException>(() =>
            this.CreateAsync(upstream: upstream)
        );

        Assert.Equal(
            expected: "unexpected result for the new issue, which may have been created: " + reported,
            actual: exception.Message
        );
        await this.DidNotWriteTheBoardAsync();
    }

    [Theory]
    [InlineData("https://ghe.example:8443/other-owner/renamed.repo/issues/7", 7)]
    [InlineData("https://github.com/credfeto/credfeto-workflow-mcp/issues/0042", 42)]
    public async Task TheHostAndRepositoryOfTheIssueUrlAreNotComparedAsync(string url, int number)
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Responses.Json("""[{"name":"Medium"}]"""),
                1 => CreatedIssue(url),
                _ => Unexpected(index),
            }
        );

        IssueCreateResponse response = await this.CreateAsync(upstream: upstream);

        Assert.Equal(expected: new Uri(url), actual: response.Url);

        // The board item is named by the repository written to, whatever the URL says
        await this.ReceivedBoardWriteAsync(owner: OWNER, repo: NAME, number: number);
    }

    [Fact]
    public async Task ARepositoryWithoutAWorkflowBoardFailsBeforeAnythingIsWrittenAsync()
    {
        WorkflowBoardNotFoundException failure = new("no Workflow board for " + REPOSITORY);
        this.BoardResolver.ResolveAsync(repository: REPOSITORY, cancellationToken: Arg.Any<CancellationToken>())
            .ThrowsAsync(failure);
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        WorkflowBoardNotFoundException exception = await Assert.ThrowsAsync<WorkflowBoardNotFoundException>(() =>
            this.CreateAsync(upstream: upstream)
        );

        Assert.Same(expected: failure, actual: exception);
        Assert.Empty(upstream.Requests);
        await this.DidNotWriteTheBoardAsync();
    }

    [Fact]
    public async Task AMissingNotStartedOptionFailsBeforeAnythingIsWrittenAsync()
    {
        StatusOptionMissingException failure = new("the Status field has no Not Started option");
        this.StatusResolver.ResolveAsync(
                repository: REPOSITORY,
                project: Project,
                state: WorkflowStates.NOT_STARTED,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        StatusOptionMissingException exception = await Assert.ThrowsAsync<StatusOptionMissingException>(() =>
            this.CreateAsync(upstream: upstream)
        );

        Assert.Same(expected: failure, actual: exception);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task ABoardWriteFailureAfterTheIssueIsCreatedCarriesItsUrlAsync()
    {
        BoardWriteException failure = new("could not add " + ISSUE_URL + " to project 7");
        this.BoardWriterFailsWith(failure);
        using RecordingHttpMessageHandler upstream = new(ExistingLabelsFlow);

        IssueCreateBoardWriteException exception = await Assert.ThrowsAsync<IssueCreateBoardWriteException>(() =>
            this.CreateAsync(upstream: upstream)
        );

        Assert.Equal(
            expected: "could not add "
                + ISSUE_URL
                + " to project 7; the issue was already created: "
                + ISSUE_URL,
            actual: exception.Message
        );
        Assert.Equal(expected: new Uri(ISSUE_URL), actual: exception.Url);
        Assert.Same(expected: failure, actual: exception.InnerException);
    }

    [Fact]
    public async Task AnUpstreamBoardFailureAfterTheIssueIsCreatedCarriesItsUrlWithAFixedMessageAsync()
    {
        HttpRequestException failure = new("upstream detail");
        this.BoardWriterFailsWith(failure);
        using RecordingHttpMessageHandler upstream = new(ExistingLabelsFlow);

        IssueCreateBoardWriteException exception = await Assert.ThrowsAsync<IssueCreateBoardWriteException>(() =>
            this.CreateAsync(upstream: upstream)
        );

        Assert.Equal(
            expected: "could not put "
                + ISSUE_URL
                + " on the Workflow board; the issue was already created: "
                + ISSUE_URL,
            actual: exception.Message
        );
        Assert.Equal(expected: new Uri(ISSUE_URL), actual: exception.Url);
    }

    private void BoardWriterFailsWith(Exception failure)
    {
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
    }

    private static void AssertIssueRequest(RecordedRequest request, string labels)
    {
        Assert.Equal(
            expected: """{"title":"feat(mcp): add issue create","body":"## Summary\n\nCreate an issue.","labels":"""
                + labels
                + "}",
            actual: request.Body
        );
    }

    private static void AssertLabelRequest(RecordedRequest request, string json)
    {
        Assert.Equal(expected: HttpMethod.Post, actual: request.Method);
        Assert.Equal(expected: LabelsUri, actual: request.RequestUri);
        Assert.Equal(expected: json, actual: request.Body);
    }
}
