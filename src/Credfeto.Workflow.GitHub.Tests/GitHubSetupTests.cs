using Credfeto.Workflow.GitHub.Authorization;
using Credfeto.Workflow.GitHub.Board;
using Credfeto.Workflow.GitHub.Branches;
using Credfeto.Workflow.GitHub.Cache;
using Credfeto.Workflow.GitHub.Checks;
using Credfeto.Workflow.GitHub.ClosingIssues;
using Credfeto.Workflow.GitHub.Comments;
using Credfeto.Workflow.GitHub.Configuration;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Issues;
using Credfeto.Workflow.GitHub.Labels;
using Credfeto.Workflow.GitHub.Leaks;
using Credfeto.Workflow.GitHub.PullRequests;
using Credfeto.Workflow.GitHub.Search;
using Credfeto.Workflow.GitHub.Status;
using FunFair.Test.Common;
using FunFair.Test.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests;

public sealed class GitHubSetupTests : DependencyInjectionTestsBase
{
    public GitHubSetupTests(ITestOutputHelper output)
        : base(output: output, dependencyInjectionRegistration: Configure) { }

    private static IServiceCollection Configure(IServiceCollection services)
    {
        return services
            .AddGitHub()
            .AddMockedService<IOptions<GitHubOptions>>(static o => o.Value.Returns(new GitHubOptions()))
            .AddMockedService<IOptions<WorkflowOptions>>(static o => o.Value.Returns(new WorkflowOptions()));
    }

    [Fact]
    public void RepositoryProjectCacheShouldBeRegistered()
    {
        this.RequireService<IRepositoryProjectCache>();
    }

    [Fact]
    public void RepositoryProjectCacheShouldBeOfCorrectType()
    {
        this.RequireServiceInCollectionFor<IRepositoryProjectCache, RepositoryProjectCache>();
    }

    [Fact]
    public void GitHubRestClientShouldBeRegistered()
    {
        this.RequireService<IGitHubRestClient>();
    }

    [Fact]
    public void GitHubRestClientShouldBeOfCorrectType()
    {
        this.RequireServiceInCollectionFor<IGitHubRestClient, GitHubRestClient>();
    }

    [Fact]
    public void GitHubGraphQlClientShouldBeRegistered()
    {
        this.RequireService<IGitHubGraphQlClient>();
    }

    [Fact]
    public void GitHubGraphQlClientShouldBeOfCorrectType()
    {
        this.RequireServiceInCollectionFor<IGitHubGraphQlClient, GitHubGraphQlClient>();
    }

    [Fact]
    public void WorkflowBoardResolverShouldBeRegistered()
    {
        this.RequireService<IWorkflowBoardResolver>();
    }

    [Fact]
    public void WorkflowBoardResolverShouldBeOfCorrectType()
    {
        this.RequireServiceInCollectionFor<IWorkflowBoardResolver, WorkflowBoardResolver>();
    }

    [Fact]
    public void WorkflowBoardReaderShouldBeRegistered()
    {
        this.RequireService<IWorkflowBoardReader>();
    }

    [Fact]
    public void WorkflowBoardReaderShouldBeOfCorrectType()
    {
        this.RequireServiceInCollectionFor<IWorkflowBoardReader, WorkflowBoardReader>();
    }

    [Fact]
    public void BoardWriterShouldBeRegistered()
    {
        this.RequireService<IBoardWriter>();
    }

    [Fact]
    public void BoardWriterShouldBeOfCorrectType()
    {
        this.RequireServiceInCollectionFor<IBoardWriter, BoardWriter>();
    }

    [Fact]
    public void WorkflowStatusResolverShouldBeRegistered()
    {
        this.RequireService<IWorkflowStatusResolver>();
    }

    [Fact]
    public void WorkflowStatusResolverShouldBeOfCorrectType()
    {
        this.RequireServiceInCollectionFor<IWorkflowStatusResolver, WorkflowStatusResolver>();
    }

    [Fact]
    public void PullRequestCommentsReaderShouldBeRegistered()
    {
        this.RequireService<IPullRequestCommentsReader>();
    }

    [Fact]
    public void PullRequestCommentsReaderShouldBeOfCorrectType()
    {
        this.RequireServiceInCollectionFor<IPullRequestCommentsReader, PullRequestCommentsReader>();
    }

    [Fact]
    public void ClosingIssueLabelsServiceShouldBeRegistered()
    {
        this.RequireService<IClosingIssueLabelsService>();
    }

    [Fact]
    public void ClosingIssueLabelsServiceShouldBeOfCorrectType()
    {
        this.RequireServiceInCollectionFor<IClosingIssueLabelsService, ClosingIssueLabelsService>();
    }

    [Fact]
    public void PullRequestServiceShouldBeRegistered()
    {
        this.RequireService<IPullRequestService>();
    }

    [Fact]
    public void PullRequestServiceShouldBeOfCorrectType()
    {
        this.RequireServiceInCollectionFor<IPullRequestService, PullRequestService>();
    }

    [Fact]
    public void PullRequestListServiceShouldBeRegistered()
    {
        this.RequireService<IPullRequestListService>();
    }

    [Fact]
    public void PullRequestListServiceShouldBeOfCorrectType()
    {
        this.RequireServiceInCollectionFor<IPullRequestListService, PullRequestListService>();
    }

    [Fact]
    public void PrCreateServiceShouldBeRegistered()
    {
        this.RequireService<IPrCreateService>();
    }

    [Fact]
    public void PrCreateServiceShouldBeOfCorrectType()
    {
        this.RequireServiceInCollectionFor<IPrCreateService, PrCreateService>();
    }

    [Fact]
    public void PrReplyServiceShouldBeRegistered()
    {
        this.RequireService<IPrReplyService>();
    }

    [Fact]
    public void PrReplyServiceShouldBeOfCorrectType()
    {
        this.RequireServiceInCollectionFor<IPrReplyService, PrReplyService>();
    }

    [Fact]
    public void CommentAddServiceShouldBeRegistered()
    {
        this.RequireService<ICommentAddService>();
    }

    [Fact]
    public void CommentAddServiceShouldBeOfCorrectType()
    {
        this.RequireServiceInCollectionFor<ICommentAddService, CommentAddService>();
    }

    [Fact]
    public void LabelServiceShouldBeRegistered()
    {
        this.RequireService<ILabelService>();
    }

    [Fact]
    public void LabelServiceShouldBeOfCorrectType()
    {
        this.RequireServiceInCollectionFor<ILabelService, LabelService>();
    }

    [Fact]
    public void IssueServiceShouldBeRegistered()
    {
        this.RequireService<IIssueService>();
    }

    [Fact]
    public void IssueServiceShouldBeOfCorrectType()
    {
        this.RequireServiceInCollectionFor<IIssueService, IssueService>();
    }

    [Fact]
    public void IssuePlanStatusReaderShouldBeRegistered()
    {
        this.RequireService<IIssuePlanStatusReader>();
    }

    [Fact]
    public void IssuePlanStatusReaderShouldBeOfCorrectType()
    {
        this.RequireServiceInCollectionFor<IIssuePlanStatusReader, IssuePlanStatusReader>();
    }

    [Fact]
    public void IssueCreateServiceShouldBeRegistered()
    {
        this.RequireService<IIssueCreateService>();
    }

    [Fact]
    public void IssueCreateServiceShouldBeOfCorrectType()
    {
        this.RequireServiceInCollectionFor<IIssueCreateService, IssueCreateService>();
    }

    [Fact]
    public void IssueSearchServiceShouldBeRegistered()
    {
        this.RequireService<IIssueSearchService>();
    }

    [Fact]
    public void IssueSearchServiceShouldBeOfCorrectType()
    {
        this.RequireServiceInCollectionFor<IIssueSearchService, IssueSearchService>();
    }

    [Fact]
    public void PrSearchServiceShouldBeRegistered()
    {
        this.RequireService<IPrSearchService>();
    }

    [Fact]
    public void PrSearchServiceShouldBeOfCorrectType()
    {
        this.RequireServiceInCollectionFor<IPrSearchService, PrSearchService>();
    }

    [Fact]
    public void CodeSearchServiceShouldBeRegistered()
    {
        this.RequireService<ICodeSearchService>();
    }

    [Fact]
    public void CodeSearchServiceShouldBeOfCorrectType()
    {
        this.RequireServiceInCollectionFor<ICodeSearchService, CodeSearchService>();
    }

    [Fact]
    public void RepoSearchServiceShouldBeRegistered()
    {
        this.RequireService<IRepoSearchService>();
    }

    [Fact]
    public void RepoSearchServiceShouldBeOfCorrectType()
    {
        this.RequireServiceInCollectionFor<IRepoSearchService, RepoSearchService>();
    }

    [Fact]
    public void IssueListServiceShouldBeRegistered()
    {
        this.RequireService<IIssueListService>();
    }

    [Fact]
    public void IssueListServiceShouldBeOfCorrectType()
    {
        this.RequireServiceInCollectionFor<IIssueListService, IssueListService>();
    }

    [Fact]
    public void BodyCheckServiceShouldBeRegistered()
    {
        this.RequireService<IBodyCheckService>();
    }

    [Fact]
    public void BodyCheckServiceShouldBeOfCorrectType()
    {
        this.RequireServiceInCollectionFor<IBodyCheckService, BodyCheckService>();
    }

    [Fact]
    public void RequiredChecksReaderShouldBeRegistered()
    {
        this.RequireService<IRequiredChecksReader>();
    }

    [Fact]
    public void RequiredChecksReaderShouldBeOfCorrectType()
    {
        this.RequireServiceInCollectionFor<IRequiredChecksReader, RequiredChecksReader>();
    }

    [Fact]
    public void PrChecksServiceShouldBeRegistered()
    {
        this.RequireService<IPrChecksService>();
    }

    [Fact]
    public void PrChecksServiceShouldBeOfCorrectType()
    {
        this.RequireServiceInCollectionFor<IPrChecksService, PrChecksService>();
    }

    [Fact]
    public void WorkflowStatusCheckServiceShouldBeRegistered()
    {
        this.RequireService<IWorkflowStatusCheckService>();
    }

    [Fact]
    public void WorkflowStatusCheckServiceShouldBeOfCorrectType()
    {
        this.RequireServiceInCollectionFor<IWorkflowStatusCheckService, WorkflowStatusCheckService>();
    }

    [Fact]
    public void WorkflowStatusSetServiceShouldBeRegistered()
    {
        this.RequireService<IWorkflowStatusSetService>();
    }

    [Fact]
    public void WorkflowStatusSetServiceShouldBeOfCorrectType()
    {
        this.RequireServiceInCollectionFor<IWorkflowStatusSetService, WorkflowStatusSetService>();
    }

    [Fact]
    public void RepositoryVisibilityLookupShouldBeRegistered()
    {
        this.RequireService<IRepositoryVisibilityLookup>();
    }

    [Fact]
    public void RepositoryVisibilityLookupShouldBeOfCorrectType()
    {
        this.RequireServiceInCollectionFor<IRepositoryVisibilityLookup, RepositoryVisibilityLookup>();
    }

    [Fact]
    public void WorkflowOptionsValidatorShouldBeOfCorrectType()
    {
        this.RequireServiceInCollectionFor<IValidateOptions<WorkflowOptions>, WorkflowOptionsValidator>();
    }

    [Fact]
    public void LeakCheckerShouldBeRegistered()
    {
        this.RequireService<ILeakChecker>();
    }

    [Fact]
    public void LeakCheckerShouldBeOfCorrectType()
    {
        this.RequireServiceInCollectionFor<ILeakChecker, LeakChecker>();
    }

    [Fact]
    public void OptionsValidatorShouldBeOfCorrectType()
    {
        this.RequireServiceInCollectionFor<IValidateOptions<GitHubOptions>, GitHubOptionsValidator>();
    }

    [Fact]
    public void HttpContextAccessorShouldBeRegistered()
    {
        this.RequireService<IHttpContextAccessor>();
    }

    [Fact]
    public void CallerTokenForwardingHandlerShouldBeRegistered()
    {
        this.RequireService<CallerTokenForwardingHandler>();
    }
}
