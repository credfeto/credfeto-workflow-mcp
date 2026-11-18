using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.Mcp.Modes;
using FunFair.Test.Common;
using ModelContextProtocol;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class WorkflowMcpLabelModeToolsTests : TestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";

    private const int ISSUE = 16;

    private const int PULL_REQUEST = 41;

    private static readonly IReadOnlyList<string> Labels = ["Bug"];

    private readonly ILabelService _service;

    private readonly IRequestToolMode _toolMode;

    public WorkflowMcpLabelModeToolsTests()
    {
        this._service = GetSubstitute<ILabelService>();
        this._toolMode = GetSubstitute<IRequestToolMode>();
        this._toolMode.Current.Returns(ToolModes.Issues);
    }

    private static LabelChangeResponse Changed(int number)
    {
        return new(Repository: REPOSITORY, Number: number, Labels: Labels);
    }

    [Fact]
    public async Task IssuesModeAddsLabelsOnlyThroughTheIssueCheckAsync()
    {
        LabelChangeResponse expected = Changed(ISSUE);
        this._service.AddToIssueAsync(
                repository: REPOSITORY,
                issue: ISSUE,
                labels: Labels,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        LabelChangeResponse actual = await this.LabelAddAsync(pr: null, issue: ISSUE);

        Assert.Same(expected: expected, actual: actual);
        await this.AssertNoUnrestrictedChangeAsync();
    }

    [Fact]
    public async Task IssuesModeRemovesLabelsOnlyThroughTheIssueCheckAsync()
    {
        LabelChangeResponse expected = Changed(ISSUE);
        this._service.RemoveFromIssueAsync(
                repository: REPOSITORY,
                issue: ISSUE,
                labels: Labels,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        LabelChangeResponse actual = await this.LabelRemoveAsync(pr: null, issue: ISSUE);

        Assert.Same(expected: expected, actual: actual);
        await this.AssertNoUnrestrictedChangeAsync();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(ISSUE)]
    public async Task IssuesModeLabelAddRefusesAPullRequestTargetAsync(int? issue)
    {
        McpException exception = await Assert.ThrowsAsync<McpException>(() =>
            this.LabelAddAsync(pr: PULL_REQUEST, issue: issue)
        );

        Assert.Equal(
            expected: "label_add cannot change the labels of a pull request in X-Mode Issues; give an issue number",
            actual: exception.Message
        );
        await this.AssertNoChangeAsync();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(ISSUE)]
    public async Task IssuesModeLabelRemoveRefusesAPullRequestTargetAsync(int? issue)
    {
        McpException exception = await Assert.ThrowsAsync<McpException>(() =>
            this.LabelRemoveAsync(pr: PULL_REQUEST, issue: issue)
        );

        Assert.Equal(
            expected: "label_remove cannot change the labels of a pull request in X-Mode Issues; give an issue number",
            actual: exception.Message
        );
        await this.AssertNoChangeAsync();
    }

    [Fact]
    public async Task IssuesModeLabelAddReportsAnIssueThatIsAPullRequestAsync()
    {
        PullRequestTargetRefusedException failure = new(REPOSITORY + "#41 is a pull request, not an issue");
        this._service.AddToIssueAsync(
                repository: REPOSITORY,
                issue: PULL_REQUEST,
                labels: Labels,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);

        McpException exception = await Assert.ThrowsAsync<McpException>(() =>
            this.LabelAddAsync(pr: null, issue: PULL_REQUEST)
        );

        Assert.Equal(
            expected: "label_add cannot change the labels of a pull request in X-Mode Issues: "
                + REPOSITORY
                + "#41 is a pull request, not an issue",
            actual: exception.Message
        );
        Assert.Same(expected: failure, actual: exception.InnerException);
    }

    [Fact]
    public async Task IssuesModeLabelRemoveReportsAnIssueThatIsAPullRequestAsync()
    {
        PullRequestTargetRefusedException failure = new(REPOSITORY + "#41 is a pull request, not an issue");
        this._service.RemoveFromIssueAsync(
                repository: REPOSITORY,
                issue: PULL_REQUEST,
                labels: Labels,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(failure);

        McpException exception = await Assert.ThrowsAsync<McpException>(() =>
            this.LabelRemoveAsync(pr: null, issue: PULL_REQUEST)
        );

        Assert.Equal(
            expected: "label_remove cannot change the labels of a pull request in X-Mode Issues: "
                + REPOSITORY
                + "#41 is a pull request, not an issue",
            actual: exception.Message
        );
        Assert.Same(expected: failure, actual: exception.InnerException);
    }

    [Theory]
    [InlineData(PULL_REQUEST, null)]
    [InlineData(null, ISSUE)]
    public async Task AllModeAddsLabelsToPullRequestsAndIssuesAsBeforeAsync(int? pr, int? issue)
    {
        this._toolMode.Current.Returns(ToolModes.All);
        LabelChangeResponse expected = Changed(pr ?? ISSUE);
        this._service.AddAsync(
                repository: REPOSITORY,
                pullRequest: pr,
                issue: issue,
                labels: Labels,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        LabelChangeResponse actual = await this.LabelAddAsync(pr: pr, issue: issue);

        Assert.Same(expected: expected, actual: actual);
        await this.AssertNoIssueOnlyChangeAsync();
    }

    [Theory]
    [InlineData(PULL_REQUEST, null)]
    [InlineData(null, ISSUE)]
    public async Task AllModeRemovesLabelsFromPullRequestsAndIssuesAsBeforeAsync(int? pr, int? issue)
    {
        this._toolMode.Current.Returns(ToolModes.All);
        LabelChangeResponse expected = Changed(pr ?? ISSUE);
        this._service.RemoveAsync(
                repository: REPOSITORY,
                pullRequest: pr,
                issue: issue,
                labels: Labels,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(expected);

        LabelChangeResponse actual = await this.LabelRemoveAsync(pr: pr, issue: issue);

        Assert.Same(expected: expected, actual: actual);
        await this.AssertNoIssueOnlyChangeAsync();
    }

    private async Task AssertNoChangeAsync()
    {
        await this.AssertNoUnrestrictedChangeAsync();
        await this.AssertNoIssueOnlyChangeAsync();
    }

    private async Task AssertNoUnrestrictedChangeAsync()
    {
        _ = await this
            ._service.DidNotReceiveWithAnyArgs()
            .AddAsync(
                repository: default!,
                pullRequest: default,
                issue: default,
                labels: default!,
                cancellationToken: this.CancellationToken()
            );
        _ = await this
            ._service.DidNotReceiveWithAnyArgs()
            .RemoveAsync(
                repository: default!,
                pullRequest: default,
                issue: default,
                labels: default!,
                cancellationToken: this.CancellationToken()
            );
    }

    private async Task AssertNoIssueOnlyChangeAsync()
    {
        _ = await this
            ._service.DidNotReceiveWithAnyArgs()
            .AddToIssueAsync(
                repository: default!,
                issue: default,
                labels: default!,
                cancellationToken: this.CancellationToken()
            );
        _ = await this
            ._service.DidNotReceiveWithAnyArgs()
            .RemoveFromIssueAsync(
                repository: default!,
                issue: default,
                labels: default!,
                cancellationToken: this.CancellationToken()
            );
    }

    private Task<LabelChangeResponse> LabelAddAsync(int? pr, int? issue)
    {
        return WorkflowMcpTools.LabelAddAsync(
            repo: REPOSITORY,
            labels: Labels,
            service: this._service,
            toolMode: this._toolMode,
            pr: pr,
            issue: issue,
            cancellationToken: this.CancellationToken()
        );
    }

    private Task<LabelChangeResponse> LabelRemoveAsync(int? pr, int? issue)
    {
        return WorkflowMcpTools.LabelRemoveAsync(
            repo: REPOSITORY,
            labels: Labels,
            service: this._service,
            toolMode: this._toolMode,
            pr: pr,
            issue: issue,
            cancellationToken: this.CancellationToken()
        );
    }
}
