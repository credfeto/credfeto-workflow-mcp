using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.PullRequests;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using NSubstitute;

namespace Credfeto.Workflow.GitHub.Tests.PullRequests;

public abstract class PrCreateServiceTestBase : GitHubClientTestBase
{
    protected const string OWNER = "credfeto";
    protected const string NAME = "credfeto-workflow-mcp";
    protected const string REPOSITORY = OWNER + "/" + NAME;
    protected const string HEAD = "feat/28-pr-create";
    protected const string BASE = "main";
    protected const string TITLE = "feat(mcp): add pr create";
    protected const string BODY = "## Summary\n\nCreate a pull request.";
    protected const string PR_URL = "https://github.com/credfeto/credfeto-workflow-mcp/pull/93";
    protected const int PR = 93;
    protected const string FIELD_ID = "PVTSSF_1";
    protected const string FIELD_NAME = "Status";
    protected const string OPTION_ID = "OPT_1";

    protected static readonly WorkflowProject Project = new(ProjectNodeId: "PVT_1", Number: 7, Owner: OWNER);

    protected static readonly Uri RepositoryUri = new(GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY);

    protected static readonly Uri BranchUri = new(
        GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/branches/" + HEAD
    );

    protected static readonly Uri PullsUri = new(GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/pulls");

    protected PrCreateServiceTestBase()
    {
        this.LeakChecker = GetSubstitute<ILeakChecker>();
        this.BoardResolver = GetSubstitute<IWorkflowBoardResolver>();
        this.StatusResolver = GetSubstitute<IWorkflowStatusResolver>();
        this.BoardReader = GetSubstitute<IWorkflowBoardReader>();
        this.BoardWriter = GetSubstitute<IBoardWriter>();
        this.PullRequestList = GetSubstitute<IPullRequestListService>();

        this.LeakChecker.CheckRepositoryReferencesAsync(
                repository: Arg.Any<string>(),
                subject: Arg.Any<LeakSubject>(),
                text: Arg.Any<string>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns((LeakRefusal?)null);
        this.BoardResolver.ResolveAsync(repository: Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Project);
        this.StatusResolver.ResolveAsync(
                repository: Arg.Any<string>(),
                project: Project,
                state: Arg.Any<string>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(new ResolvedStatusOption(FieldId: FIELD_ID, FieldName: FIELD_NAME, OptionId: OPTION_ID));
        this.OpenPullRequests();
    }

    protected ILeakChecker LeakChecker { get; }

    protected IWorkflowBoardResolver BoardResolver { get; }

    protected IWorkflowStatusResolver StatusResolver { get; }

    protected IWorkflowBoardReader BoardReader { get; }

    protected IBoardWriter BoardWriter { get; }

    protected IPullRequestListService PullRequestList { get; }

    protected static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    protected static HttpResponseMessage BranchFound()
    {
        return Responses.Json("{\"name\":\"" + HEAD + "\"}");
    }

    protected static HttpResponseMessage CreatedPullRequest(string url)
    {
        return Responses.Json("{\"html_url\":\"" + url + "\"}");
    }

    // The base is given, so the flow is the head check then the create
    protected static HttpResponseMessage CreateFlow(int index)
    {
        return index switch
        {
            0 => BranchFound(),
            1 => CreatedPullRequest(PR_URL),
            _ => Unexpected(index),
        };
    }

    protected static PullRequestListEntry Entry(
        string head = HEAD,
        string url = PR_URL,
        int number = PR,
        IReadOnlyList<string>? flags = null
    )
    {
        return new(
            Number: number,
            Head: head,
            Base: BASE,
            Author: OWNER,
            State: "open",
            Flags: flags ?? [],
            Url: new(url),
            Title: TITLE
        );
    }

    protected void OpenPullRequests(params PullRequestListEntry[] entries)
    {
        this.PullRequestList.ListAsync(
                repository: Arg.Any<string>(),
                issue: Arg.Any<int?>(),
                head: Arg.Any<string?>(),
                labels: Arg.Any<IReadOnlyList<string>?>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(new PullRequestListResponse(Repository: REPOSITORY, PullRequests: entries));
    }

    protected void BoardStatus(BoardItemState state, string? name)
    {
        this.BoardReader.ReadStatusAsync(
                owner: OWNER,
                repo: NAME,
                kind: WorkItemKind.PULL_REQUEST,
                number: PR,
                project: Project,
                field: Arg.Any<WorkflowStatusField>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(new BoardItemStatus(State: state, Name: name));
    }

    private protected Task<PrCreateResponse> CreateAsync(
        RecordingHttpMessageHandler upstream,
        string repository = REPOSITORY,
        string head = HEAD,
        string? baseBranch = BASE,
        string title = TITLE,
        string body = BODY
    )
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        PrCreateService service = new(
            restClient: rest,
            leakChecker: this.LeakChecker,
            boardResolver: this.BoardResolver,
            statusResolver: this.StatusResolver,
            boardReader: this.BoardReader,
            boardWriter: this.BoardWriter,
            pullRequestList: this.PullRequestList
        );

        return service
            .CreateAsync(
                repository: repository,
                head: head,
                baseBranch: baseBranch,
                title: title,
                body: body,
                cancellationToken: this.CancellationToken()
            )
            .AsTask();
    }

    protected Task ReceivedBoardWriteAsync()
    {
        return this
            .BoardWriter.Received(1)
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

    protected Task DidNotWriteTheBoardAsync()
    {
        return this
            .BoardWriter.DidNotReceiveWithAnyArgs()
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
}
