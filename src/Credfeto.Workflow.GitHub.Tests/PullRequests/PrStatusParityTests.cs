using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Board;
using Credfeto.Workflow.GitHub.Checks;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.PullRequests;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using NSubstitute;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.PullRequests;

public sealed class PrStatusParityTests : GitHubClientTestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const int PR = 73;
    private const string REFERENCE = REPOSITORY + "#73";
    private const string SHA = "0123456789abcdef0123456789abcdef01234567";
    private const long APP_ID = 15368;

    private const string PULL_JSON =
        "{\"head\":{\"ref\":\"feature\",\"sha\":\""
        + SHA
        + "\"},\"base\":{\"ref\":\"main\",\"sha\":\"fedcba9876543210fedcba9876543210fedcba98\"}}";

    private const string RUNS_JSON =
        "{\"check_runs\":["
        + "{\"id\":1,\"name\":\"build\",\"status\":\"completed\",\"conclusion\":\"failure\",\"completed_at\":\"2026-01-01T00:00:00Z\",\"app\":{\"id\":15368}},"
        + "{\"id\":2,\"name\":\"lint\",\"status\":\"in_progress\",\"conclusion\":null,\"completed_at\":null,\"app\":{\"id\":15368}},"
        + "{\"id\":3,\"name\":\"test\",\"status\":\"completed\",\"conclusion\":\"success\",\"completed_at\":\"2026-01-01T00:00:00Z\",\"app\":{\"id\":15368}}"
        + "]}";

    private const string STATUSES_JSON = "{\"statuses\":[]}";

    private static readonly IReadOnlyList<RequiredCheck> Required =
    [
        new(Name: "build", AppId: APP_ID),
        new(Name: "lint", AppId: APP_ID),
        new(Name: "test", AppId: APP_ID),
    ];

    private static readonly WorkflowProject Project = new(ProjectNodeId: "PVT_1", Number: 7, Owner: "credfeto");

    [Fact]
    public async Task TheStatusAgreesWithTheChecksAndTheBoardAsync()
    {
        using RecordingHttpMessageHandler directUpstream = ChecksFlow();
        using RecordingHttpMessageHandler statusUpstream = ChecksFlow();

        IWorkflowStatusCheckService statusCheck = CreateStatusCheck();

        PrChecksResponse direct = await this.CreateChecks(directUpstream)
            .ReadAsync(
                repository: REPOSITORY,
                pullRequest: PR,
                sha: null,
                branch: null,
                all: false,
                cancellationToken: this.CancellationToken()
            );
        WorkflowStatusCheckResult board = await statusCheck.CheckAsync(
            repository: REPOSITORY,
            pullRequest: PR,
            issue: null,
            cancellationToken: this.CancellationToken()
        );

        PrStatusResponse status = await CreateStatus(
                checks: this.CreateChecks(statusUpstream),
                statusCheck: statusCheck
            )
            .ReadAsync(repository: REPOSITORY, pullRequest: PR, cancellationToken: this.CancellationToken());

        Assert.Equal(expected: direct.Sha, actual: status.HeadSha);
        Assert.Equal(expected: direct.Verdict, actual: status.Checks.Verdict);
        Assert.Equal(expected: Names(checks: direct, verdict: PrChecksVerdict.Failed), actual: status.Checks.Failing);
        Assert.Equal(expected: Names(checks: direct, verdict: PrChecksVerdict.Pending), actual: status.Checks.Pending);
        Assert.Equal(expected: ["build (app 15368)"], actual: status.Checks.Failing);
        Assert.Equal(expected: ["lint (app 15368)"], actual: status.Checks.Pending);
        Assert.Equal(expected: WorkflowStatusCheckOutcome.Found, actual: board.Outcome);
        Assert.Equal(expected: board.State, actual: status.Board);
    }

    private static IReadOnlyList<string> Names(PrChecksResponse checks, PrChecksVerdict verdict)
    {
        return [.. checks.Checks.Where(check => check.Verdict == verdict).Select(check => check.Name)];
    }

    private static RecordingHttpMessageHandler ChecksFlow()
    {
        Func<HttpResponseMessage>[] responses =
        [
            () => Responses.Json(PULL_JSON),
            () => Responses.Json(RUNS_JSON),
            () => Responses.Json(STATUSES_JSON),
        ];

        return new(index => responses[index]());
    }

    private PrChecksService CreateChecks(RecordingHttpMessageHandler upstream)
    {
        IRequiredChecksReader reader = GetSubstitute<IRequiredChecksReader>();
        Configure(
            call: reader.ReadRequiredChecksAsync(
                repository: Arg.Any<string>(),
                branch: Arg.Any<string>(),
                cancellationToken: Arg.Any<CancellationToken>()
            ),
            result: new(Required)
        );

        return new(
            restClient: new GitHubRestClient(
                httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
                logger: this.GetTypedLogger<GitHubRestClient>()
            ),
            requiredChecksReader: reader
        );
    }

    private static WorkflowStatusCheckService CreateStatusCheck()
    {
        IWorkflowBoardResolver resolver = GetSubstitute<IWorkflowBoardResolver>();
        _ = resolver
            .ResolveAsync(repository: Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Project);

        IWorkflowBoardReader reader = GetSubstitute<IWorkflowBoardReader>();
        _ = reader
            .ReadStatusAsync(
                owner: Arg.Any<string>(),
                repo: Arg.Any<string>(),
                kind: Arg.Any<WorkItemKind>(),
                number: Arg.Any<int>(),
                project: Arg.Any<WorkflowProject>(),
                field: Arg.Any<WorkflowStatusField>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(new BoardItemStatus(State: BoardItemState.STATE, Name: "AI Review"));

        return new(boardResolver: resolver, boardReader: reader);
    }

    private static void Configure<T>(ValueTask<T> call, ValueTask<T> result)
    {
        _ = call.Returns(result);
    }

    private static PrStatusService CreateStatus(IPrChecksService checks, IWorkflowStatusCheckService statusCheck)
    {
        return new(
            viewService: CreateView(),
            checksService: checks,
            statusCheckService: statusCheck,
            commentsReader: CreateComments(),
            readyAtService: CreateReadyAt()
        );
    }

    private static IPullRequestService CreateView()
    {
        IPullRequestService view = GetSubstitute<IPullRequestService>();
        _ = view.ViewAsync(
                repository: Arg.Any<string>(),
                pullRequest: Arg.Any<int>(),
                fields: Arg.Any<IReadOnlyList<string>?>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(
                new PullRequestView(
                    Pr: REFERENCE,
                    State: "OPEN",
                    Draft: "no",
                    Title: "Add the status tool",
                    Url: new("https://github.com/" + REPOSITORY + "/pull/73"),
                    Base: "main",
                    Head: "feature",
                    HeadSha: SHA,
                    AutoMerge: "off",
                    ReviewDecision: "REVIEW_REQUIRED",
                    MergeState: "BLOCKED",
                    Mergeable: "MERGEABLE",
                    CreatedAt: null,
                    MergedAt: null,
                    MergeCommit: null,
                    Labels: string.Empty,
                    Assignees: string.Empty,
                    Author: "credfeto",
                    CommitAuthors: null,
                    ChangedFiles: 1,
                    Files: null,
                    Body: null
                )
            );

        return view;
    }

    private static IPullRequestCommentsReader CreateComments()
    {
        IPullRequestCommentsReader comments = GetSubstitute<IPullRequestCommentsReader>();
        _ = comments
            .ReadAsync(
                repository: Arg.Any<string>(),
                pullRequest: Arg.Any<int>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(
                new PullRequestComments(
                    Pr: REFERENCE,
                    ReviewDecision: "REVIEW_REQUIRED",
                    Comments: [],
                    Reviews: [],
                    Threads: [],
                    Summary: new(Comments: 0, Reviews: 0, ReviewsEmpty: 0, Threads: 0, Unresolved: 0, Inline: 0)
                )
            );

        return comments;
    }

    private static IPrReadyAtService CreateReadyAt()
    {
        IPrReadyAtService readyAt = GetSubstitute<IPrReadyAtService>();
        _ = readyAt
            .ReadAsync(
                repository: Arg.Any<string>(),
                pullRequest: Arg.Any<int>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(new PrReadyAtResponse(ReadyAt: "2026-01-01T00:00:00Z", Source: PrReadyAtService.SOURCE_CREATED));

        return readyAt;
    }
}
