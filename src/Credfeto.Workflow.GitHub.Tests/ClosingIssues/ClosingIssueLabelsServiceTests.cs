using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.ClosingIssues;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.ClosingIssues;

public sealed class ClosingIssueLabelsServiceTests : GitHubClientTestBase
{
    private const string REPOSITORY = "o/r";
    private const int PULL_REQUEST = 5;
    private const string FAILURE_MESSAGE = "could not read the closing issues of o/r#5";
    private const string NO_CURSOR = "null";

    [Fact]
    public async Task ReadAsyncReturnsSortedUniqueLabelsWithoutWorkflowControlLabelsAsync()
    {
        string issue1 = Issue(number: 1, labels: Labels("false", NO_CURSOR, "bug", "Blocked", "feature"));
        string issue2 = Issue(
            number: 2,
            labels: "{\"pageInfo\":{\"hasNextPage\":false,\"endCursor\":null},\"nodes\":[{\"name\":\"bug\"},{\"name\":\"On-Hold\"},{\"name\":\"alpha\"},{\"name\":\"Zeta\"},{\"name\":null},{\"name\":\"\"},null]}"
        );
        string page = ClosingPage(hasNextPage: "false", endCursor: NO_CURSOR, nodes: "[" + issue1 + "," + issue2 + "]");

        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(page));

        ClosingIssueLabelsResponse response = await this.ReadAsync(upstream);

        Assert.Equal(expected: ["Zeta", "alpha", "bug", "feature"], actual: response.Labels);
        Assert.True(condition: response.Complete, userMessage: "Every closing issue should have been read");
        Assert.Empty(response.FailedReferences);
        _ = Assert.Single(upstream.Requests);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData(
        "[{\"number\":1,\"repository\":{\"owner\":{\"login\":\"o\"},\"name\":\"r\"},\"labels\":{\"pageInfo\":{\"hasNextPage\":false,\"endCursor\":null},\"nodes\":null}}]"
    )]
    public async Task ReadAsyncReturnsNoLabelsWhenThereAreNoneAsync(string nodes)
    {
        string page = ClosingPage(hasNextPage: "false", endCursor: NO_CURSOR, nodes: nodes);

        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(page));

        ClosingIssueLabelsResponse response = await this.ReadAsync(upstream);

        Assert.Empty(response.Labels);
        Assert.True(condition: response.Complete, userMessage: "Every closing issue should have been read");
        Assert.Empty(response.FailedReferences);
    }

    [Fact]
    public async Task ReadAsyncSendsTheRepositoryAndPullRequestAsync()
    {
        string page = ClosingPage(hasNextPage: "false", endCursor: NO_CURSOR, nodes: "[]");

        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(page));

        _ = await this.ReadAsync(upstream);

        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: HttpMethod.Post, actual: request.Method);

        using JsonDocument body = JsonDocument.Parse(request.Body ?? string.Empty);
        JsonElement variables = body.RootElement.GetProperty("variables");
        Assert.Equal(expected: "o", actual: variables.GetProperty("o").GetString());
        Assert.Equal(expected: "r", actual: variables.GetProperty("r").GetString());
        Assert.Equal(expected: PULL_REQUEST, actual: variables.GetProperty("n").GetInt32());
        Assert.Equal(expected: JsonValueKind.Null, actual: variables.GetProperty("after").ValueKind);
        Assert.Contains(
            expectedSubstring: "closingIssuesReferences(first:100,after:$after)",
            actualString: body.RootElement.GetProperty("query").GetString(),
            comparisonType: StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task ReadAsyncFollowsClosingIssuePagesAsync()
    {
        string first = ClosingPage(
            hasNextPage: "true",
            endCursor: Cursor("c1"),
            nodes: "[" + Issue(number: 1, labels: Labels("false", NO_CURSOR, "first")) + "]"
        );
        string second = ClosingPage(
            hasNextPage: "false",
            endCursor: NO_CURSOR,
            nodes: "[" + Issue(number: 2, labels: Labels("false", NO_CURSOR, "second")) + "]"
        );

        using RecordingHttpMessageHandler upstream = new(index => Responses.Json(index == 0 ? first : second));

        ClosingIssueLabelsResponse response = await this.ReadAsync(upstream);

        Assert.Equal(expected: ["first", "second"], actual: response.Labels);
        Assert.True(condition: response.Complete, userMessage: "Every closing issue should have been read");
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);

        using JsonDocument body = JsonDocument.Parse(upstream.Requests[1].Body ?? string.Empty);
        Assert.Equal(
            expected: "c1",
            actual: body.RootElement.GetProperty("variables").GetProperty("after").GetString()
        );
    }

    [Fact]
    public async Task ReadAsyncFollowsIssueLabelPagesAsync()
    {
        string closing = ClosingPage(
            hasNextPage: "false",
            endCursor: NO_CURSOR,
            nodes: "[" + Issue(number: 7, labels: Labels("true", Cursor("c1"), "a")) + "]"
        );
        string labels = LabelsPage(Labels("false", NO_CURSOR, "b"));

        using RecordingHttpMessageHandler upstream = new(index => Responses.Json(index == 0 ? closing : labels));

        ClosingIssueLabelsResponse response = await this.ReadAsync(upstream);

        Assert.Equal(expected: ["a", "b"], actual: response.Labels);
        Assert.True(condition: response.Complete, userMessage: "Every closing issue should have been read");
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);

        using JsonDocument body = JsonDocument.Parse(upstream.Requests[1].Body ?? string.Empty);
        JsonElement variables = body.RootElement.GetProperty("variables");
        Assert.Equal(expected: "o", actual: variables.GetProperty("o").GetString());
        Assert.Equal(expected: "r", actual: variables.GetProperty("r").GetString());
        Assert.Equal(expected: 7, actual: variables.GetProperty("n").GetInt32());
        Assert.Equal(expected: "c1", actual: variables.GetProperty("after").GetString());
        Assert.Contains(
            expectedSubstring: "issue(number:$n){labels(first:100,after:$after)",
            actualString: body.RootElement.GetProperty("query").GetString(),
            comparisonType: StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad")]
    [InlineData("a/b/c")]
    public async Task ReadAsyncRejectsAnInvalidRepositoryAsync(string repository)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Status(HttpStatusCode.OK));

        _ = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.CreateService(upstream)
                .ReadAsync(
                    repository: repository,
                    pullRequest: PULL_REQUEST,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ReadAsyncRejectsAnInvalidPullRequestNumberAsync(int pullRequest)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Status(HttpStatusCode.OK));

        _ = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.CreateService(upstream)
                .ReadAsync(
                    repository: REPOSITORY,
                    pullRequest: pullRequest,
                    cancellationToken: this.CancellationToken()
                )
                .AsTask()
        );

        Assert.Empty(upstream.Requests);
    }

    [Theory]
    [InlineData("null", "<unknown>")]
    [InlineData("{\"number\":1,\"repository\":null,\"labels\":null}", "<unknown>")]
    [InlineData("{\"number\":1,\"repository\":{\"owner\":null,\"name\":\"r\"},\"labels\":null}", "<unknown>")]
    [InlineData(
        "{\"number\":1,\"repository\":{\"owner\":{\"login\":\"o\"},\"name\":null},\"labels\":null}",
        "<unknown>"
    )]
    [InlineData("{\"repository\":{\"owner\":{\"login\":\"o\"},\"name\":\"r\"},\"labels\":null}", "<unknown>")]
    [InlineData("{\"number\":0,\"repository\":{\"owner\":{\"login\":\"o\"},\"name\":\"r\"},\"labels\":null}", "o/r#0")]
    [InlineData(
        "{\"number\":1,\"repository\":{\"owner\":{\"login\":\"o\"},\"name\":\"r x\"},\"labels\":null}",
        "o/r x#1"
    )]
    [InlineData("{\"number\":1,\"repository\":{\"owner\":{\"login\":\"o\"},\"name\":\"r\"},\"labels\":null}", "o/r#1")]
    [InlineData(
        "{\"number\":1,\"repository\":{\"owner\":{\"login\":\"o\"},\"name\":\"r\"},\"labels\":{\"pageInfo\":null,\"nodes\":[{\"name\":\"lost\"}]}}",
        "o/r#1"
    )]
    [InlineData(
        "{\"number\":1,\"repository\":{\"owner\":{\"login\":\"o\"},\"name\":\"r\"},\"labels\":{\"pageInfo\":{\"hasNextPage\":null,\"endCursor\":null},\"nodes\":[{\"name\":\"lost\"}]}}",
        "o/r#1"
    )]
    [InlineData(
        "{\"number\":1,\"repository\":{\"owner\":{\"login\":\"o\"},\"name\":\"r\"},\"labels\":{\"pageInfo\":{\"hasNextPage\":true,\"endCursor\":null},\"nodes\":[{\"name\":\"lost\"}]}}",
        "o/r#1"
    )]
    [InlineData(
        "{\"number\":1,\"repository\":{\"owner\":{\"login\":\"o\"},\"name\":\"r\"},\"labels\":{\"pageInfo\":{\"hasNextPage\":true,\"endCursor\":\"bad cursor\"},\"nodes\":[{\"name\":\"lost\"}]}}",
        "o/r#1"
    )]
    public async Task ReadAsyncReportsAnUnreadableIssueAsync(string node, string expectedReference)
    {
        string healthy = Issue(number: 2, labels: Labels("false", NO_CURSOR, "bug"));
        string page = ClosingPage(hasNextPage: "false", endCursor: NO_CURSOR, nodes: "[" + node + "," + healthy + "]");

        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(page));

        ClosingIssueLabelsResponse response = await this.ReadAsync(upstream);

        Assert.Equal(expected: ["bug"], actual: response.Labels);
        Assert.False(condition: response.Complete, userMessage: "An unreadable closing issue should be reported");
        Assert.Equal(expected: [expectedReference], actual: response.FailedReferences);
        _ = Assert.Single(upstream.Requests);
    }

    [Theory]
    [InlineData("graphql")]
    [InlineData("html")]
    [InlineData("status")]
    public async Task ReadAsyncReportsAnIssueWhoseLabelPageFailsAsync(string failure)
    {
        string closing = ClosingPage(
            hasNextPage: "false",
            endCursor: NO_CURSOR,
            nodes: "["
                + Issue(number: 1, labels: Labels("true", Cursor("c1"), "lost"))
                + ","
                + Issue(number: 2, labels: Labels("false", NO_CURSOR, "bug"))
                + "]"
        );

        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0 ? Responses.Json(closing) : Failure(failure)
        );

        ClosingIssueLabelsResponse response = await this.ReadAsync(upstream);

        Assert.Equal(expected: ["bug"], actual: response.Labels);
        Assert.False(condition: response.Complete, userMessage: "An unreadable closing issue should be reported");
        Assert.Equal(expected: ["o/r#1"], actual: response.FailedReferences);
    }

    [Theory]
    [InlineData("{\"data\":{\"repository\":null}}")]
    [InlineData("{\"data\":{\"repository\":{\"issue\":null}}}")]
    [InlineData("{\"data\":{\"repository\":{\"issue\":{\"labels\":null}}}}")]
    public async Task ReadAsyncReportsAnIssueWhoseLabelPageIsIncompleteAsync(string labelPage)
    {
        string closing = ClosingPage(
            hasNextPage: "false",
            endCursor: NO_CURSOR,
            nodes: "[" + Issue(number: 1, labels: Labels("true", Cursor("c1"), "lost")) + "]"
        );

        using RecordingHttpMessageHandler upstream = new(index => Responses.Json(index == 0 ? closing : labelPage));

        ClosingIssueLabelsResponse response = await this.ReadAsync(upstream);

        Assert.Empty(response.Labels);
        Assert.False(condition: response.Complete, userMessage: "An unreadable closing issue should be reported");
        Assert.Equal(expected: ["o/r#1"], actual: response.FailedReferences);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task ReadAsyncReportsAnIssueWhoseLabelCursorRepeatsAsync()
    {
        string closing = ClosingPage(
            hasNextPage: "false",
            endCursor: NO_CURSOR,
            nodes: "[" + Issue(number: 1, labels: Labels("true", Cursor("c1"), "a")) + "]"
        );
        string labels = LabelsPage(Labels("true", Cursor("c1"), "b"));

        using RecordingHttpMessageHandler upstream = new(index => Responses.Json(index == 0 ? closing : labels));

        ClosingIssueLabelsResponse response = await this.ReadAsync(upstream);

        Assert.Empty(response.Labels);
        Assert.False(condition: response.Complete, userMessage: "An unreadable closing issue should be reported");
        Assert.Equal(expected: ["o/r#1"], actual: response.FailedReferences);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task ReadAsyncReportsAnIssueWithTooManyLabelPagesAsync()
    {
        string closing = ClosingPage(
            hasNextPage: "false",
            endCursor: NO_CURSOR,
            nodes: "[" + Issue(number: 1, labels: Labels("true", Cursor("c0"), "a")) + "]"
        );

        using RecordingHttpMessageHandler upstream = new(index =>
            Responses.Json(
                index == 0
                    ? closing
                    : LabelsPage(Labels("true", Cursor("c" + index.ToString(CultureInfo.InvariantCulture)), "b"))
            )
        );

        ClosingIssueLabelsResponse response = await this.ReadAsync(upstream);

        Assert.Empty(response.Labels);
        Assert.False(condition: response.Complete, userMessage: "An unreadable closing issue should be reported");
        Assert.Equal(expected: ["o/r#1"], actual: response.FailedReferences);
        Assert.Equal(expected: 101, actual: upstream.Requests.Count);
    }

    [Theory]
    [InlineData("{\"data\":{\"repository\":null}}")]
    [InlineData("{\"data\":{\"repository\":{\"pullRequest\":null}}}")]
    [InlineData("{\"data\":{\"repository\":{\"pullRequest\":{\"closingIssuesReferences\":null}}}}")]
    [InlineData(
        "{\"data\":{\"repository\":{\"pullRequest\":{\"closingIssuesReferences\":{\"pageInfo\":null,\"nodes\":[]}}}}}"
    )]
    [InlineData(
        "{\"data\":{\"repository\":{\"pullRequest\":{\"closingIssuesReferences\":{\"pageInfo\":{\"hasNextPage\":null,\"endCursor\":null},\"nodes\":[]}}}}}"
    )]
    [InlineData(
        "{\"data\":{\"repository\":{\"pullRequest\":{\"closingIssuesReferences\":{\"pageInfo\":{\"hasNextPage\":true,\"endCursor\":null},\"nodes\":[]}}}}}"
    )]
    [InlineData(
        "{\"data\":{\"repository\":{\"pullRequest\":{\"closingIssuesReferences\":{\"pageInfo\":{\"hasNextPage\":true,\"endCursor\":\"bad cursor\"},\"nodes\":[]}}}}}"
    )]
    public async Task ReadAsyncFailsWhenTheClosingIssuesCannotBeReadAsync(string page)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(page));

        ClosingIssueLabelsFailedException exception = await this.ReadFailureAsync(upstream);

        Assert.Equal(expected: FAILURE_MESSAGE, actual: exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public async Task ReadAsyncFailsWhenTheClosingIssueCursorRepeatsAsync()
    {
        string page = ClosingPage(hasNextPage: "true", endCursor: Cursor("c1"), nodes: "[]");

        using RecordingHttpMessageHandler upstream = new(_ => Responses.Json(page));

        ClosingIssueLabelsFailedException exception = await this.ReadFailureAsync(upstream);

        Assert.Equal(expected: FAILURE_MESSAGE, actual: exception.Message);
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task ReadAsyncFailsWhenThereAreTooManyClosingIssuePagesAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            Responses.Json(
                ClosingPage(
                    hasNextPage: "true",
                    endCursor: Cursor("c" + index.ToString(CultureInfo.InvariantCulture)),
                    nodes: "[]"
                )
            )
        );

        ClosingIssueLabelsFailedException exception = await this.ReadFailureAsync(upstream);

        Assert.Equal(expected: FAILURE_MESSAGE, actual: exception.Message);
        Assert.Equal(expected: 100, actual: upstream.Requests.Count);
    }

    [Theory]
    [InlineData("graphql", typeof(GitHubGraphQlException))]
    [InlineData("html", typeof(GitHubInvalidResponseException))]
    [InlineData("status", typeof(HttpRequestException))]
    public async Task ReadAsyncFailsWithAFixedMessageWhenTheUpstreamFailsAsync(string failure, Type innerType)
    {
        using RecordingHttpMessageHandler upstream = new(_ => Failure(failure));

        ClosingIssueLabelsFailedException exception = await this.ReadFailureAsync(upstream);

        Assert.Equal(expected: FAILURE_MESSAGE, actual: exception.Message);
        Assert.IsType(expectedType: innerType, @object: exception.InnerException);
    }

    private static HttpResponseMessage Failure(string failure)
    {
        return failure switch
        {
            "graphql" => Responses.Json("""{"errors":[{"message":"x"}],"data":null}"""),
            "html" => Responses.Html("<html/>"),
            _ => Responses.Status(HttpStatusCode.BadGateway),
        };
    }

    private static string Cursor(string value)
    {
        return "\"" + value + "\"";
    }

    private static string ClosingPage(string hasNextPage, string endCursor, string nodes)
    {
        return "{\"data\":{\"repository\":{\"pullRequest\":{\"closingIssuesReferences\":{\"pageInfo\":{\"hasNextPage\":"
            + hasNextPage
            + ",\"endCursor\":"
            + endCursor
            + "},\"nodes\":"
            + nodes
            + "}}}}}";
    }

    private static string Issue(int number, string labels)
    {
        return "{\"number\":"
            + number.ToString(CultureInfo.InvariantCulture)
            + ",\"repository\":{\"owner\":{\"login\":\"o\"},\"name\":\"r\"},\"labels\":"
            + labels
            + "}";
    }

    private static string Labels(string hasNextPage, string endCursor, params string[] names)
    {
        return "{\"pageInfo\":{\"hasNextPage\":"
            + hasNextPage
            + ",\"endCursor\":"
            + endCursor
            + "},\"nodes\":["
            + string.Join(separator: ',', values: names.Select(static name => "{\"name\":\"" + name + "\"}"))
            + "]}";
    }

    private static string LabelsPage(string labels)
    {
        return "{\"data\":{\"repository\":{\"issue\":{\"labels\":" + labels + "}}}}";
    }

    private ClosingIssueLabelsService CreateService(HttpMessageHandler upstream)
    {
        GitHubGraphQlClient client = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            logger: this.GetTypedLogger<GitHubGraphQlClient>()
        );

        return new(graphQlClient: client);
    }

    private ValueTask<ClosingIssueLabelsResponse> ReadAsync(HttpMessageHandler upstream)
    {
        return this.CreateService(upstream)
            .ReadAsync(repository: REPOSITORY, pullRequest: PULL_REQUEST, cancellationToken: this.CancellationToken());
    }

    private Task<ClosingIssueLabelsFailedException> ReadFailureAsync(HttpMessageHandler upstream)
    {
        return Assert.ThrowsAsync<ClosingIssueLabelsFailedException>(() => this.ReadAsync(upstream).AsTask());
    }
}
