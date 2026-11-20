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
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.PullRequests;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.PullRequests;

public sealed class PrSyncLabelsServiceTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const int PULL_REQUEST = 87;
    private const string REFERENCE = REPOSITORY + "#87";
    private const string PULL_REQUEST_URL = "https://github.com/credfeto/credfeto-workflow-mcp/pull/87";
    private const string AGENT = "steward-warden";
    private const string OTHER = "someone-else";

    private static readonly Uri PullRequestUri = new(GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/pulls/87");

    private static readonly Uri IssueUri = new(GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/issues/87");

    private static readonly Uri IssueLabelsUri = new(
        GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/issues/87/labels"
    );

    private static readonly Uri UserUri = new(GitHubServices.ApiBaseUrl + "user");

    private readonly IClosingIssueLabelsService _closingIssueLabels;

    private readonly ILabelListService _labelList;

    public PrSyncLabelsServiceTests()
    {
        this._closingIssueLabels = GetSubstitute<IClosingIssueLabelsService>();
        this._labelList = GetSubstitute<ILabelListService>();
        this.RepositoryLabels("bug", "Enhancement", "High", "Urgent", "Blocked", "On Hold");
    }

    [Fact]
    public async Task SyncAddsEveryClosingIssueLabelPriorityIncludedOneAtATimeAndRemovesNothingAsync()
    {
        this.ClosingLabels("Enhancement", "Urgent", "bug");
        using RecordingHttpMessageHandler upstream = Flow(PullRequest, LabelsAdded, LabelsAdded, LabelsAdded);

        PrSyncLabelsResult result = await this.SyncAsync(upstream);

        Assert.Equal(expected: PrSyncLabelsOutcome.Synced, actual: result.Outcome);
        Assert.Equal(expected: REFERENCE + " synced", actual: result.Message);
        Assert.Equal(expected: new Uri(PULL_REQUEST_URL), actual: result.Url);
        Assert.Equal(expected: ["Enhancement", "Urgent", "bug"], actual: result.Added);
        Assert.Empty(result.Skipped);
        Assert.Empty(result.FailedReferences);
        Assert.False(condition: result.Assigned, userMessage: "assignSelf was not asked for");
        Assert.Null(result.Rule);
        AssertLabelPosts(upstream: upstream, labels: ["Enhancement", "Urgent", "bug"]);
        Assert.DoesNotContain(upstream.Requests, static request =>
            request.Method == HttpMethod.Delete || request.Method == HttpMethod.Patch
        );
    }

    [Fact]
    public async Task SyncWritesEachLabelInTheRepositorySpellingMatchedWithoutRegardToCaseAsync()
    {
        this.ClosingLabels("BUG", "enhancement", "high");
        using RecordingHttpMessageHandler upstream = Flow(PullRequest, LabelsAdded, LabelsAdded, LabelsAdded);

        PrSyncLabelsResult result = await this.SyncAsync(upstream);

        Assert.Equal(expected: ["bug", "Enhancement", "High"], actual: result.Added);
        AssertLabelPosts(upstream: upstream, labels: ["bug", "Enhancement", "High"]);
    }

    [Fact]
    public async Task SyncAddsALabelOnceWhenTheClosingIssuesSpellItDifferentlyAsync()
    {
        this.ClosingLabels("Bug", "bug");
        using RecordingHttpMessageHandler upstream = Flow(PullRequest, LabelsAdded);

        PrSyncLabelsResult result = await this.SyncAsync(upstream);

        Assert.Equal(expected: ["bug"], actual: result.Added);
        AssertLabelPosts(upstream: upstream, labels: ["bug"]);
    }

    [Fact]
    public async Task SyncSkipsAndReportsALabelTheRepositoryDoesNotHaveAndStillAddsTheOthersAsync()
    {
        this.ClosingLabels("bug", "only-elsewhere", "Urgent");
        using RecordingHttpMessageHandler upstream = Flow(PullRequest, LabelsAdded, LabelsAdded);

        PrSyncLabelsResult result = await this.SyncAsync(upstream);

        Assert.Equal(expected: PrSyncLabelsOutcome.Synced, actual: result.Outcome);
        Assert.Equal(expected: ["bug", "Urgent"], actual: result.Added);
        Assert.Equal(expected: ["only-elsewhere"], actual: result.Skipped);
        AssertLabelPosts(upstream: upstream, labels: ["bug", "Urgent"]);
    }

    [Theory]
    [InlineData("Blocked")]
    [InlineData("blocked")]
    [InlineData("On Hold")]
    [InlineData("ON HOLD")]
    [InlineData("On-Hold")]
    [InlineData("on-hold")]
    public async Task SyncNeverAddsBlockedOrOnHoldAsync(string label)
    {
        this.ClosingLabels(label, "bug");
        using RecordingHttpMessageHandler upstream = Flow(PullRequest, LabelsAdded);

        PrSyncLabelsResult result = await this.SyncAsync(upstream);

        Assert.Equal(expected: ["bug"], actual: result.Added);
        Assert.Empty(result.Skipped);
        AssertLabelPosts(upstream: upstream, labels: ["bug"]);
    }

    [Fact]
    public async Task SyncWithNoLabelsToAddReadsNoRepositoryLabelsAndWritesNothingAsync()
    {
        this.ClosingLabels("Blocked");
        using RecordingHttpMessageHandler upstream = Flow(PullRequest);

        PrSyncLabelsResult result = await this.SyncAsync(upstream);

        Assert.Equal(expected: PrSyncLabelsOutcome.Synced, actual: result.Outcome);
        Assert.Empty(result.Added);
        _ = Assert.Single(upstream.Requests);
        await this
            ._labelList.DidNotReceive()
            .ListAsync(repository: Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncReportsAPartialReadAsIncompleteAfterAddingTheLabelsThatWereReadAsync()
    {
        this.ClosingLabelsRead(complete: false, failedReferences: ["other/repo#3"], "bug");
        using RecordingHttpMessageHandler upstream = Flow(PullRequest, LabelsAdded);

        PrSyncLabelsResult result = await this.SyncAsync(upstream);

        Assert.Equal(expected: PrSyncLabelsOutcome.Incomplete, actual: result.Outcome);
        Assert.Equal(
            expected: "the labels of a closing issue of "
                + REFERENCE
                + " could not be read, so only those read were added",
            actual: result.Message
        );
        Assert.Equal(expected: ["bug"], actual: result.Added);
        Assert.Equal(expected: ["other/repo#3"], actual: result.FailedReferences);
        AssertLabelPosts(upstream: upstream, labels: ["bug"]);
    }

    [Fact]
    public async Task SyncReportsAFailurePartWayWithTheLabelsAlreadyAddedAsync()
    {
        this.ClosingLabels("Enhancement", "Urgent", "bug");
        using RecordingHttpMessageHandler upstream = Flow(PullRequest, LabelsAdded, Rejected);

        PrSyncLabelsResult result = await this.SyncAsync(upstream);

        Assert.Equal(expected: PrSyncLabelsOutcome.Failed, actual: result.Outcome);
        Assert.Equal(
            expected: "could not add the label Urgent to " + REFERENCE + ": GitHub answered with status 422",
            actual: result.Message
        );
        Assert.Equal(expected: ["Enhancement"], actual: result.Added);
        AssertLabelPosts(upstream: upstream, labels: ["Enhancement", "Urgent"]);
    }

    [Fact]
    public async Task SyncWithAssignSelfAddsTheAgentAfterTheLabelsAsync()
    {
        this.ClosingLabels("bug");
        using RecordingHttpMessageHandler upstream = Flow(PullRequest, User, LabelsAdded, Edited);

        PrSyncLabelsResult result = await this.SyncAsync(upstream: upstream, assignSelf: true);

        Assert.Equal(expected: PrSyncLabelsOutcome.Synced, actual: result.Outcome);
        Assert.True(condition: result.Assigned, userMessage: "The agent should have been assigned");
        Assert.Equal(expected: ["bug"], actual: result.Added);
        RecordedRequest assign = upstream.Requests[3];
        Assert.Equal(expected: HttpMethod.Patch, actual: assign.Method);
        Assert.Equal(expected: IssueUri, actual: assign.RequestUri);
        Assert.Equal(expected: "{\"assignees\":[\"" + AGENT + "\"]}", actual: assign.Body);
    }

    [Fact]
    public async Task SyncWithAssignSelfWritesNoAssigneeWhenTheAgentAlreadyHoldsThePullRequestAsync()
    {
        this.ClosingLabels("bug");
        using RecordingHttpMessageHandler upstream = Flow(
            () => PullRequestAssignedTo("Steward-Warden"),
            User,
            LabelsAdded
        );

        PrSyncLabelsResult result = await this.SyncAsync(upstream: upstream, assignSelf: true);

        Assert.Equal(expected: PrSyncLabelsOutcome.Synced, actual: result.Outcome);
        Assert.True(condition: result.Assigned, userMessage: "The agent already holds the pull request");
        Assert.Equal(expected: 3, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task SyncWithAssignSelfRefusesAPullRequestAssignedToAnotherUserBeforeWritingAnythingAsync()
    {
        this.ClosingLabels("bug", "Urgent");
        using RecordingHttpMessageHandler upstream = Flow(() => PullRequestAssignedTo(OTHER), User);

        PrSyncLabelsResult result = await this.SyncAsync(upstream: upstream, assignSelf: true);

        Assert.Equal(expected: PrSyncLabelsOutcome.Refused, actual: result.Outcome);
        Assert.Equal(expected: "already assigned to another user", actual: result.Rule);
        Assert.Equal(
            expected: "assignSelf refused: already assigned to another user; nothing was written",
            actual: result.Message
        );
        Assert.Empty(result.Added);
        Assert.False(condition: result.Assigned, userMessage: "Nothing should have been assigned");
        Assert.All(upstream.Requests, static request => Assert.Equal(expected: HttpMethod.Get, actual: request.Method));
        Assert.Equal(expected: UserUri, actual: upstream.Requests[1].RequestUri);
        await this
            ._labelList.DidNotReceive()
            .ListAsync(repository: Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncWithAssignSelfReportsAFailedAssignAfterTheLabelsAsync()
    {
        this.ClosingLabels("bug");
        using RecordingHttpMessageHandler upstream = Flow(PullRequest, User, LabelsAdded, Rejected);

        PrSyncLabelsResult result = await this.SyncAsync(upstream: upstream, assignSelf: true);

        Assert.Equal(expected: PrSyncLabelsOutcome.Failed, actual: result.Outcome);
        Assert.Equal(
            expected: "could not assign " + AGENT + " to " + REFERENCE + ": GitHub answered with status 422",
            actual: result.Message
        );
        Assert.Equal(expected: ["bug"], actual: result.Added);
        Assert.False(condition: result.Assigned, userMessage: "The assign failed");
    }

    [Fact]
    public async Task SyncWithAssignSelfFailsBeforeWritingWhenTheLoginCannotBeReadAsync()
    {
        this.ClosingLabels("bug");
        using RecordingHttpMessageHandler upstream = Flow(PullRequest, () => Responses.Json("{\"login\":null}"));

        PrSyncLabelsFailedException exception = await Assert.ThrowsAsync<PrSyncLabelsFailedException>(() =>
            this.SyncAsync(upstream: upstream, assignSelf: true)
        );

        Assert.Equal(
            expected: "could not read the signed-in account, so nothing was written to " + REFERENCE,
            actual: exception.Message
        );
        Assert.Equal(expected: 2, actual: upstream.Requests.Count);
    }

    [Fact]
    public async Task SyncReportsAPullRequestThatIsNotFoundBeforeReadingAnythingElseAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(() => Responses.Status(HttpStatusCode.NotFound));

        PullRequestNotFoundException exception = await Assert.ThrowsAsync<PullRequestNotFoundException>(() =>
            this.SyncAsync(upstream)
        );

        Assert.Equal(
            expected: "the pull request " + REFERENCE + " was not found, or the caller cannot see it",
            actual: exception.Message
        );
        await this.DidNotReadClosingIssuesAsync();
    }

    [Theory]
    [InlineData("{\"number\":86,\"html_url\":\"" + PULL_REQUEST_URL + "\",\"title\":\"t\",\"assignees\":[]}")]
    [InlineData("{\"number\":87,\"html_url\":\"https://github.com/x/y/issues/87\",\"title\":\"t\",\"assignees\":[]}")]
    [InlineData("{\"number\":87,\"html_url\":\"" + PULL_REQUEST_URL + "\",\"title\":\"t\"}")]
    public async Task SyncFailsBeforeWritingWhenThePullRequestAnswerCannotBeTrustedAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = Flow(() => Responses.Json(json));

        PrSyncLabelsFailedException exception = await Assert.ThrowsAsync<PrSyncLabelsFailedException>(() =>
            this.SyncAsync(upstream)
        );

        Assert.Equal(
            expected: "could not read the pull request " + REFERENCE + ", so nothing was written",
            actual: exception.Message
        );
        await this.DidNotReadClosingIssuesAsync();
    }

    [Fact]
    public async Task SyncFailsBeforeWritingWhenThePullRequestCannotBeReadAsync()
    {
        using RecordingHttpMessageHandler upstream = Flow(() => throw new HttpRequestException("upstream detail"));

        PrSyncLabelsFailedException exception = await Assert.ThrowsAsync<PrSyncLabelsFailedException>(() =>
            this.SyncAsync(upstream)
        );

        Assert.Equal(
            expected: "could not read the pull request " + REFERENCE + ", so nothing was written",
            actual: exception.Message
        );
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task SyncPassesOnAFailureToReadTheClosingIssuesWithoutWritingAsync()
    {
        ClosingIssueLabelsFailedException failure = new("could not read the closing issues of " + REFERENCE);
        this._closingIssueLabels.ReadAsync(
                repository: REPOSITORY,
                pullRequest: PULL_REQUEST,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);
        using RecordingHttpMessageHandler upstream = Flow(PullRequest);

        ClosingIssueLabelsFailedException exception = await Assert.ThrowsAsync<ClosingIssueLabelsFailedException>(() =>
            this.SyncAsync(upstream)
        );

        Assert.Same(expected: failure, actual: exception);
        _ = Assert.Single(upstream.Requests);
    }

    [Fact]
    public async Task SyncPassesOnAFailureToListTheRepositoryLabelsWithoutWritingAsync()
    {
        this.ClosingLabels("bug");
        LabelListFailedException failure = new("could not list the labels of " + REPOSITORY);
        this._labelList.ListAsync(repository: REPOSITORY, cancellationToken: Arg.Any<CancellationToken>())
            .ThrowsAsync(failure);
        using RecordingHttpMessageHandler upstream = Flow(PullRequest);

        LabelListFailedException exception = await Assert.ThrowsAsync<LabelListFailedException>(() =>
            this.SyncAsync(upstream)
        );

        Assert.Same(expected: failure, actual: exception);
        _ = Assert.Single(upstream.Requests);
    }

    [Theory]
    [InlineData("not-a-repository", PULL_REQUEST)]
    [InlineData(REPOSITORY, 0)]
    [InlineData(REPOSITORY, -1)]
    public async Task SyncRefusesInvalidArgumentsBeforeAnyCallAsync(string repository, int pullRequest)
    {
        using RecordingHttpMessageHandler upstream = Flow();

        await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.SyncAsync(upstream: upstream, repository: repository, pullRequest: pullRequest)
        );

        Assert.Empty(upstream.Requests);
        await this.DidNotReadClosingIssuesAsync();
    }

    private static void AssertLabelPosts(RecordingHttpMessageHandler upstream, IReadOnlyList<string> labels)
    {
        RecordedRequest[] posts = [.. upstream.Requests.Where(static request => request.Method == HttpMethod.Post)];

        Assert.Equal(expected: labels.Count, actual: posts.Length);
        Assert.All(posts, static post => Assert.Equal(expected: IssueLabelsUri, actual: post.RequestUri));
        Assert.Equal(
            expected: [.. labels.Select(static label => "{\"labels\":[\"" + label + "\"]}")],
            actual: posts.Select(static post => post.Body ?? string.Empty),
            comparer: StringComparer.Ordinal
        );
        Assert.Equal(expected: PullRequestUri, actual: upstream.Requests[0].RequestUri);
    }

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private static RecordingHttpMessageHandler Flow(params Func<HttpResponseMessage>[] responses)
    {
        return new(index => index < responses.Length ? responses[index]() : Unexpected(index));
    }

    private static HttpResponseMessage PullRequest()
    {
        return PullRequestWith(string.Empty);
    }

    private static HttpResponseMessage PullRequestAssignedTo(string login)
    {
        return PullRequestWith("{\"login\":\"" + login + "\"}");
    }

    private static HttpResponseMessage PullRequestWith(string assignees)
    {
        return Responses.Json(
            "{\"number\":87,\"html_url\":\""
                + PULL_REQUEST_URL
                + "\",\"title\":\"feat: sync\",\"body\":null,\"assignees\":["
                + assignees
                + "],\"labels\":[{\"name\":\"Development\"}]}"
        );
    }

    private static HttpResponseMessage User()
    {
        return Responses.Json("{\"login\":\"" + AGENT + "\"}");
    }

    private static HttpResponseMessage LabelsAdded()
    {
        return Responses.Json("[{\"name\":\"bug\"}]");
    }

    private static HttpResponseMessage Edited()
    {
        return Responses.Json("{\"number\":87,\"html_url\":\"" + PULL_REQUEST_URL + "\"}");
    }

    private static HttpResponseMessage Rejected()
    {
        return Responses.StatusWithJson(
            statusCode: HttpStatusCode.UnprocessableEntity,
            json: "{\"message\":\"Validation Failed\"}"
        );
    }

    private void RepositoryLabels(params string[] labels)
    {
        this._labelList.ListAsync(repository: REPOSITORY, cancellationToken: Arg.Any<CancellationToken>())
            .Returns(new LabelListResponse(Repository: REPOSITORY, Labels: labels));
    }

    private void ClosingLabels(params string[] labels)
    {
        this.ClosingLabelsRead(complete: true, failedReferences: [], labels);
    }

    private void ClosingLabelsRead(bool complete, IReadOnlyList<string> failedReferences, params string[] labels)
    {
        this._closingIssueLabels.ReadAsync(
                repository: REPOSITORY,
                pullRequest: PULL_REQUEST,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(
                new ClosingIssueLabelsResponse(Labels: labels, Complete: complete, FailedReferences: failedReferences)
            );
    }

    private async Task DidNotReadClosingIssuesAsync()
    {
        await this
            ._closingIssueLabels.DidNotReceive()
            .ReadAsync(
                repository: Arg.Any<string>(),
                pullRequest: Arg.Any<int>(),
                cancellationToken: Arg.Any<CancellationToken>()
            );
    }

    private async Task<PrSyncLabelsResult> SyncAsync(
        RecordingHttpMessageHandler upstream,
        bool assignSelf = false,
        string repository = REPOSITORY,
        int pullRequest = PULL_REQUEST
    )
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            options: GitHubOptionsWith(),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        PrSyncLabelsService service = new(
            restClient: rest,
            closingIssueLabels: this._closingIssueLabels,
            labelList: this._labelList
        );

        return await service.SyncAsync(
            repository: repository,
            pullRequest: pullRequest,
            assignSelf: assignSelf,
            cancellationToken: this.CancellationToken()
        );
    }
}
