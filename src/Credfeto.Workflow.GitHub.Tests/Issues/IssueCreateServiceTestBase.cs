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
using Credfeto.Workflow.GitHub.Issues;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using NSubstitute;

namespace Credfeto.Workflow.GitHub.Tests.Issues;

public abstract class IssueCreateServiceTestBase : GitHubClientTestBase
{
    protected const string OWNER = "credfeto";
    protected const string NAME = "credfeto-workflow-mcp";
    protected const string REPOSITORY = OWNER + "/" + NAME;
    protected const string TITLE = "feat(mcp): add issue create";
    protected const string BODY = "## Summary\n\nCreate an issue.";
    protected const string ISSUE_URL = "https://github.com/credfeto/credfeto-workflow-mcp/issues/90";
    protected const int ISSUE = 90;
    protected const string FIELD_ID = "PVTSSF_1";
    protected const string FIELD_NAME = "Status";
    protected const string OPTION_ID = "OPT_1";

    protected static readonly WorkflowProject Project = new(ProjectNodeId: "PVT_1", Number: 7, Owner: OWNER);

    protected static readonly Uri LabelsUri = new(GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/labels");

    protected static readonly Uri IssuesUri = new(GitHubServices.ApiBaseUrl + "repos/" + REPOSITORY + "/issues");

    protected IssueCreateServiceTestBase()
    {
        this.LeakChecker = GetSubstitute<ILeakChecker>();
        this.BoardResolver = GetSubstitute<IWorkflowBoardResolver>();
        this.StatusResolver = GetSubstitute<IWorkflowStatusResolver>();
        this.BoardWriter = GetSubstitute<IBoardWriter>();

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
    }

    protected ILeakChecker LeakChecker { get; }

    protected IWorkflowBoardResolver BoardResolver { get; }

    protected IWorkflowStatusResolver StatusResolver { get; }

    protected IBoardWriter BoardWriter { get; }

    protected static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    protected static HttpResponseMessage CreatedIssue(string url)
    {
        return Responses.Json("{\"html_url\":\"" + url + "\"}");
    }

    // Every label the tests use already exists, so the flow is one label listing then the issue create
    protected static HttpResponseMessage ExistingLabelsFlow(int index)
    {
        return index switch
        {
            0 => Responses.Json("""[{"name":"Medium"},{"name":"Urgent"},{"name":"Bug"},{"name":"custom"}]"""),
            1 => CreatedIssue(ISSUE_URL),
            _ => Unexpected(index),
        };
    }

    private protected Task<IssueCreateResponse> CreateAsync(
        RecordingHttpMessageHandler upstream,
        string repository = REPOSITORY,
        string priority = "Medium",
        string title = TITLE,
        string body = BODY,
        IReadOnlyList<string>? labels = null
    )
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        IssueCreateService service = new(
            restClient: rest,
            leakChecker: this.LeakChecker,
            boardResolver: this.BoardResolver,
            statusResolver: this.StatusResolver,
            boardWriter: this.BoardWriter
        );

        return service
            .CreateAsync(
                repository: repository,
                priority: priority,
                title: title,
                body: body,
                labels: labels,
                cancellationToken: this.CancellationToken()
            )
            .AsTask();
    }

    protected Task ReceivedBoardWriteAsync(string owner, string repo, int number)
    {
        return this
            .BoardWriter.Received(1)
            .SetItemStatusAsync(
                owner: owner,
                repo: repo,
                kind: WorkItemKind.ISSUE,
                number: number,
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
