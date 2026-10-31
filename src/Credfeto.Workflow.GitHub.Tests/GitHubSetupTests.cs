using Credfeto.Workflow.GitHub.Authorization;
using Credfeto.Workflow.GitHub.Board;
using Credfeto.Workflow.GitHub.Branches;
using Credfeto.Workflow.GitHub.Cache;
using Credfeto.Workflow.GitHub.Configuration;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.PullRequests;
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
            .AddMockedService<IOptions<GitHubOptions>>(static o => o.Value.Returns(new GitHubOptions()));
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
