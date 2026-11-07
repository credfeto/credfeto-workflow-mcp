using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using FunFair.Test.Common;
using ModelContextProtocol;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class WorkflowMcpSearchToolsTests : TestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string OWNER = "credfeto";
    private const string SEARCH_TEXT = "search";
    private const int DEFAULT_LIMIT = 30;

    private readonly ICodeSearchService _codeSearchService;
    private readonly IPrSearchService _prSearchService;
    private readonly IRepoSearchService _repoSearchService;

    public WorkflowMcpSearchToolsTests()
    {
        this._prSearchService = GetSubstitute<IPrSearchService>();
        this._codeSearchService = GetSubstitute<ICodeSearchService>();
        this._repoSearchService = GetSubstitute<IRepoSearchService>();
    }

    private static IReadOnlyList<Exception> CommonFailures()
    {
        return
        [
            ParameterValidationException.For(parameterName: "limit", rule: "it must be from 1 to 100"),
            new IssueSearchRefusedException("text refused: token"),
        ];
    }

    private static RepositoryNotFoundException RepositoryNotFound()
    {
        return new("the repository " + REPOSITORY + " was not found, or the caller cannot see it");
    }

    private static async Task AssertReportedAsync(Exception failure, Func<Task> search)
    {
        McpException exception = await Assert.ThrowsAsync<McpException>(search);

        Assert.Equal(expected: failure.Message, actual: exception.Message);
        Assert.Same(expected: failure, actual: exception.InnerException);
    }

    [Fact]
    public async Task PrSearchSearchesOpenPullRequestsWithTheDefaultLimitWhenNeitherIsGivenAsync()
    {
        PrSearchResult expected = PrResult();
        this._prSearchService.SearchAsync(
                repository: REPOSITORY,
                text: SEARCH_TEXT,
                state: PrSearchState.OPEN,
                limit: DEFAULT_LIMIT,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        PrSearchResult actual = await this.PrSearchAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Theory]
    [InlineData("open", PrSearchState.OPEN)]
    [InlineData("closed", PrSearchState.CLOSED)]
    [InlineData("merged", PrSearchState.MERGED)]
    [InlineData("all", PrSearchState.ALL)]
    public async Task PrSearchPassesTheGivenStateAndLimitAsync(string state, PrSearchState expectedState)
    {
        PrSearchResult expected = PrResult();
        this._prSearchService.SearchAsync(
                repository: REPOSITORY,
                text: SEARCH_TEXT,
                state: expectedState,
                limit: 5,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        PrSearchResult actual = await WorkflowMcpTools.PrSearchAsync(
            repo: REPOSITORY,
            text: SEARCH_TEXT,
            service: this._prSearchService,
            state: state,
            limit: 5,
            cancellationToken: this.CancellationToken()
        );

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task PrSearchRejectsAnUnknownStateWithoutSearchingAsync()
    {
        McpException exception = await Assert.ThrowsAsync<McpException>(() =>
            WorkflowMcpTools.PrSearchAsync(
                repo: REPOSITORY,
                text: SEARCH_TEXT,
                service: this._prSearchService,
                state: "draft",
                cancellationToken: this.CancellationToken()
            )
        );

        ParameterValidationException inner = Assert.IsType<ParameterValidationException>(exception.InnerException);
        Assert.Equal(expected: inner.Message, actual: exception.Message);
        Assert.Empty(this._prSearchService.ReceivedCalls());
    }

    [Fact]
    public async Task PrSearchFailuresAreReportedToTheCallerAsync()
    {
        IReadOnlyList<Exception> failures =
        [
            .. CommonFailures(),
            new PrSearchFailedException("could not search the pull requests of " + REPOSITORY),
            RepositoryNotFound(),
        ];

        foreach (Exception failure in failures)
        {
            this.PrSearchFailsWith(failure);

            await AssertReportedAsync(failure: failure, search: this.PrSearchAsync);
        }
    }

    [Fact]
    public async Task PrSearchOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.PrSearchFailsWith(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.PrSearchAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    [Fact]
    public async Task CodeSearchSearchesWithTheDefaultLimitWhenNoneIsGivenAsync()
    {
        CodeSearchResult expected = CodeResult();
        this._codeSearchService.SearchAsync(
                repository: REPOSITORY,
                text: SEARCH_TEXT,
                limit: DEFAULT_LIMIT,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        CodeSearchResult actual = await this.CodeSearchAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task CodeSearchPassesTheGivenLimitAsync()
    {
        CodeSearchResult expected = CodeResult();
        this._codeSearchService.SearchAsync(
                repository: REPOSITORY,
                text: SEARCH_TEXT,
                limit: 5,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        CodeSearchResult actual = await WorkflowMcpTools.CodeSearchAsync(
            repo: REPOSITORY,
            text: SEARCH_TEXT,
            service: this._codeSearchService,
            limit: 5,
            cancellationToken: this.CancellationToken()
        );

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task CodeSearchFailuresAreReportedToTheCallerAsync()
    {
        IReadOnlyList<Exception> failures =
        [
            .. CommonFailures(),
            new CodeSearchFailedException("could not search the code of " + REPOSITORY),
            RepositoryNotFound(),
        ];

        foreach (Exception failure in failures)
        {
            this.CodeSearchFailsWith(failure);

            await AssertReportedAsync(failure: failure, search: this.CodeSearchAsync);
        }
    }

    [Fact]
    public async Task CodeSearchOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.CodeSearchFailsWith(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.CodeSearchAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    [Fact]
    public async Task RepoSearchExcludesArchivedWithTheDefaultLimitWhenNeitherIsGivenAsync()
    {
        RepoSearchResult expected = RepoResult();
        this._repoSearchService.SearchAsync(
                owner: OWNER,
                text: SEARCH_TEXT,
                includeArchived: false,
                limit: DEFAULT_LIMIT,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        RepoSearchResult actual = await this.RepoSearchAsync();

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task RepoSearchPassesTheGivenArchivedAndLimitAsync()
    {
        RepoSearchResult expected = RepoResult();
        this._repoSearchService.SearchAsync(
                owner: OWNER,
                text: SEARCH_TEXT,
                includeArchived: true,
                limit: 5,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        RepoSearchResult actual = await WorkflowMcpTools.RepoSearchAsync(
            owner: OWNER,
            text: SEARCH_TEXT,
            service: this._repoSearchService,
            includeArchived: true,
            limit: 5,
            cancellationToken: this.CancellationToken()
        );

        Assert.Same(expected: expected, actual: actual);
    }

    [Fact]
    public async Task RepoSearchFailuresAreReportedToTheCallerAsync()
    {
        IReadOnlyList<Exception> failures =
        [
            .. CommonFailures(),
            new RepoSearchFailedException("could not search the repositories of " + OWNER),
            new OwnerNotFoundException("the owner " + OWNER + " was not found, or the caller cannot search it"),
        ];

        foreach (Exception failure in failures)
        {
            this.RepoSearchFailsWith(failure);

            await AssertReportedAsync(failure: failure, search: this.RepoSearchAsync);
        }
    }

    [Fact]
    public async Task RepoSearchOtherFailuresAreNotReportedToTheCallerAsync()
    {
        GitHubInvalidResponseException failure = new("upstream detail");
        this.RepoSearchFailsWith(failure);

        GitHubInvalidResponseException exception = await Assert.ThrowsAsync<GitHubInvalidResponseException>(
            this.RepoSearchAsync
        );

        Assert.Same(expected: failure, actual: exception);
    }

    private static PrSearchResult PrResult()
    {
        return new(
            Repository: REPOSITORY,
            PullRequests: [new PrSearchEntry(Number: 101, State: "OPEN", HeadBranch: "-", Title: "search tools")],
            Truncated: true
        );
    }

    private static CodeSearchResult CodeResult()
    {
        return new(
            Repository: REPOSITORY,
            Files: [new CodeSearchEntry(Path: "README.md", Matches: 2)],
            Truncated: false
        );
    }

    private static RepoSearchResult RepoResult()
    {
        return new(
            Owner: OWNER,
            Repositories:
            [
                new RepoSearchEntry(
                    Repository: REPOSITORY,
                    Visibility: "PUBLIC",
                    Status: "active",
                    DefaultBranch: "main"
                ),
            ],
            Truncated: false
        );
    }

    private void PrSearchFailsWith(Exception failure)
    {
        this._prSearchService.SearchAsync(
                repository: REPOSITORY,
                text: SEARCH_TEXT,
                state: PrSearchState.OPEN,
                limit: DEFAULT_LIMIT,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);
    }

    private void CodeSearchFailsWith(Exception failure)
    {
        this._codeSearchService.SearchAsync(
                repository: REPOSITORY,
                text: SEARCH_TEXT,
                limit: DEFAULT_LIMIT,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);
    }

    private void RepoSearchFailsWith(Exception failure)
    {
        this._repoSearchService.SearchAsync(
                owner: OWNER,
                text: SEARCH_TEXT,
                includeArchived: false,
                limit: DEFAULT_LIMIT,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);
    }

    private Task<PrSearchResult> PrSearchAsync()
    {
        return WorkflowMcpTools.PrSearchAsync(
            repo: REPOSITORY,
            text: SEARCH_TEXT,
            service: this._prSearchService,
            cancellationToken: this.CancellationToken()
        );
    }

    private Task<CodeSearchResult> CodeSearchAsync()
    {
        return WorkflowMcpTools.CodeSearchAsync(
            repo: REPOSITORY,
            text: SEARCH_TEXT,
            service: this._codeSearchService,
            cancellationToken: this.CancellationToken()
        );
    }

    private Task<RepoSearchResult> RepoSearchAsync()
    {
        return WorkflowMcpTools.RepoSearchAsync(
            owner: OWNER,
            text: SEARCH_TEXT,
            service: this._repoSearchService,
            cancellationToken: this.CancellationToken()
        );
    }
}
