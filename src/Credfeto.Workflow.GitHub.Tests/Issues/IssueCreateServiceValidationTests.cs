using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Validation;
using NSubstitute;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Issues;

public sealed class IssueCreateServiceValidationTests : IssueCreateServiceTestBase
{
    private const string PRIORITY_LIST = "Security, Urgent, High, Medium or Low";

    [Theory]
    [InlineData("credfeto")]
    [InlineData("credfeto/repo/extra")]
    [InlineData("credfeto/..")]
    public async Task AnInvalidRepositoryIsRejectedAsync(string repository)
    {
        ParameterValidationException exception = await this.AssertRejectedAsync(repository: repository);

        Assert.Equal(expected: "repository", actual: exception.ParameterName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AMissingPriorityIsRejectedAsync(string priority)
    {
        ParameterValidationException exception = await this.AssertRejectedAsync(priority: priority);

        Assert.Equal(expected: "--priority is required (" + PRIORITY_LIST + ")", actual: exception.Message);
        Assert.Equal(expected: "priority", actual: exception.ParameterName);
    }

    [Theory]
    [InlineData("Critical")]
    [InlineData("Medium ")]
    [InlineData("Bug")]
    public async Task AnUnknownPriorityIsRejectedAsync(string priority)
    {
        ParameterValidationException exception = await this.AssertRejectedAsync(priority: priority);

        Assert.Equal(expected: "unknown --priority (expected " + PRIORITY_LIST + ")", actual: exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("​")]
    [InlineData("feat: a\u0007b")]
    [InlineData("feat: line\nbreak")]
    public async Task ATitleBreakingTheCommonRulesIsRejectedAsync(string title)
    {
        ParameterValidationException exception = await this.AssertRejectedAsync(title: title);

        Assert.Equal(expected: TitleValidator.INVALID_TITLE_MESSAGE, actual: exception.Message);
        Assert.Equal(expected: "title", actual: exception.ParameterName);
    }

    [Fact]
    public async Task ATitleLongerThan256CharactersIsRejectedAsync()
    {
        ParameterValidationException exception = await this.AssertRejectedAsync(title: "feat: " + new string('a', 251));

        Assert.Equal(expected: TitleValidator.INVALID_TITLE_MESSAGE, actual: exception.Message);
    }

    [Theory]
    [InlineData("credfeto/credfeto-workflow-mcp")]
    [InlineData("funfair-tech/some-repo")]
    [InlineData("Credfeto/credfeto-workflow-mcp")]
    public async Task ATitleWithoutThePrefixIsRejectedForTheListedOwnersAsync(string repository)
    {
        ParameterValidationException exception = await this.AssertRejectedAsync(
            repository: repository,
            title: "Add an issue create tool"
        );

        Assert.Equal(expected: TitleValidator.INVALID_ISSUE_TITLE_MESSAGE, actual: exception.Message);
    }

    [Fact]
    public async Task ATitleWithoutThePrefixIsAcceptedForAnyOtherOwnerAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index switch
            {
                0 => Responses.Json("""[{"name":"Medium"}]"""),
                1 => CreatedIssue("https://github.com/octocat/hello/issues/3"),
                _ => Unexpected(index),
            }
        );

        await this.CreateAsync(upstream: upstream, repository: "octocat/hello", title: "Add an issue create tool");

        await this.ReceivedBoardWriteAsync(owner: "octocat", repo: "hello", number: 3);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \n\t ")]
    [InlineData("​ ")]
    public async Task ABlankBodyIsRejectedAsync(string body)
    {
        ParameterValidationException exception = await this.AssertRejectedAsync(body: body);

        Assert.Equal(expected: "the body is empty", actual: exception.Message);
        Assert.Equal(expected: "body", actual: exception.ParameterName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("a,b")]
    [InlineData("bell\u0007")]
    [InlineData("​")]
    [InlineData("123456789012345678901234567890123456789012345678901")]
    public async Task AnInvalidLabelIsRejectedAsync(string label)
    {
        ParameterValidationException exception = await this.AssertRejectedAsync(labels: ["Bug", label]);

        Assert.Equal(expected: "labels", actual: exception.ParameterName);
    }

    [Fact]
    public async Task AnInvisibleLabelIsRejectedForHavingNoVisibleCharacterAsync()
    {
        ParameterValidationException exception = await this.AssertRejectedAsync(labels: ["​‌"]);

        Assert.Equal(expected: "it must contain a visible character", actual: exception.Rule);
    }

    [Theory]
    [InlineData("high", "High")]
    [InlineData("Medium", "Medium")]
    [InlineData("SECURITY", "Security")]
    public async Task APriorityGivenAsALabelIsRejectedAsync(string label, string canonical)
    {
        ParameterValidationException exception = await this.AssertRejectedAsync(labels: ["Bug", label]);

        Assert.Equal(
            expected: "'" + label + "' is a priority; use --priority " + canonical + ", not --label",
            actual: exception.Message
        );
    }

    [Theory]
    [InlineData(LeakRule.TOKEN, "token")]
    [InlineData(LeakRule.HOST_PATH, "host path")]
    [InlineData(LeakRule.CONTROL_CHARACTER, "control character")]
    [InlineData(LeakRule.INVALID_UTF8, "invalid UTF-8")]
    [InlineData(LeakRule.TOO_LONG, "too long")]
    [InlineData(LeakRule.PRIVATE_REPOSITORY, "private repository")]
    public async Task ARefusedBodyNamesTheRuleAndLineAsync(LeakRule rule, string name)
    {
        this.LeakChecker.CheckBody(BODY).Returns(new LeakRefusal(Subject: LeakSubject.BODY, Rule: rule, LineNumber: 2));

        IssueCreateRefusedException exception = await this.AssertRefusedAsync();

        Assert.Equal(expected: "body refused: " + name + " (line 2)", actual: exception.Message);
        Assert.Equal(expected: "body", actual: exception.Subject);
        Assert.Equal(expected: name, actual: exception.Rule);
        Assert.Equal(expected: 2, actual: exception.LineNumber);
    }

    [Fact]
    public async Task ABodyRefusedAsAWholeHasNoLineAsync()
    {
        this.LeakChecker.CheckBody(BODY)
            .Returns(new LeakRefusal(Subject: LeakSubject.BODY, Rule: LeakRule.TOO_LONG, LineNumber: null));

        IssueCreateRefusedException exception = await this.AssertRefusedAsync();

        Assert.Equal(expected: "body refused: too long", actual: exception.Message);
        Assert.Null(exception.LineNumber);
    }

    [Fact]
    public async Task ABodyNamingAPrivateRepositoryIsRefusedAsync()
    {
        this.ReferencesRefused(subject: LeakSubject.BODY, text: BODY, lineNumber: 3);

        IssueCreateRefusedException exception = await this.AssertRefusedAsync();

        Assert.Equal(expected: "body refused: private repository (line 3)", actual: exception.Message);
    }

    [Fact]
    public async Task ARefusedTitleNamesOnlyTheRuleAsync()
    {
        this.LeakChecker.CheckValue(subject: LeakSubject.TITLE, value: TITLE)
            .Returns(new LeakRefusal(Subject: LeakSubject.TITLE, Rule: LeakRule.HOST_PATH, LineNumber: 1));

        IssueCreateRefusedException exception = await this.AssertRefusedAsync();

        Assert.Equal(expected: "title refused: host path", actual: exception.Message);
        Assert.Equal(expected: "title", actual: exception.Subject);
        Assert.Null(exception.LineNumber);
    }

    [Fact]
    public async Task ATitleNamingAPrivateRepositoryIsRefusedAsync()
    {
        this.ReferencesRefused(subject: LeakSubject.TITLE, text: TITLE, lineNumber: 1);

        IssueCreateRefusedException exception = await this.AssertRefusedAsync();

        Assert.Equal(expected: "title refused: private repository", actual: exception.Message);
    }

    [Fact]
    public async Task ARefusedLabelNamesOnlyTheRuleAsync()
    {
        this.LeakChecker.CheckValue(subject: LeakSubject.LABEL, value: "leaky")
            .Returns(new LeakRefusal(Subject: LeakSubject.LABEL, Rule: LeakRule.TOKEN, LineNumber: 1));

        IssueCreateRefusedException exception = await this.AssertRefusedAsync(labels: ["Bug", "leaky"]);

        Assert.Equal(expected: "label refused: token", actual: exception.Message);
        Assert.Equal(expected: "label", actual: exception.Subject);
    }

    [Fact]
    public async Task ALabelNamingAPrivateRepositoryIsRefusedAsync()
    {
        this.ReferencesRefused(subject: LeakSubject.LABEL, text: "credfeto/secret", lineNumber: 1);

        IssueCreateRefusedException exception = await this.AssertRefusedAsync(labels: ["credfeto/secret"]);

        Assert.Equal(expected: "label refused: private repository", actual: exception.Message);
    }

    [Fact]
    public async Task EveryGivenLabelIsLeakCheckedButNotThePriorityAsync()
    {
        using RecordingHttpMessageHandler upstream = new(ExistingLabelsFlow);

        await this.CreateAsync(upstream: upstream, labels: ["bug", "custom"]);

        this.LeakChecker.Received(1).CheckBody(BODY);
        this.LeakChecker.Received(1).CheckValue(subject: LeakSubject.TITLE, value: TITLE);
        this.LeakChecker.Received(1).CheckValue(subject: LeakSubject.LABEL, value: "Bug");
        this.LeakChecker.Received(1).CheckValue(subject: LeakSubject.LABEL, value: "custom");
        this.LeakChecker.DidNotReceive().CheckValue(subject: LeakSubject.LABEL, value: "Medium");
        await this
            .LeakChecker.Received(1)
            .CheckRepositoryReferencesAsync(
                repository: REPOSITORY,
                subject: LeakSubject.BODY,
                text: BODY,
                cancellationToken: Arg.Any<CancellationToken>()
            );
    }

    private void ReferencesRefused(LeakSubject subject, string text, int lineNumber)
    {
        this.LeakChecker.CheckRepositoryReferencesAsync(
                repository: REPOSITORY,
                subject: subject,
                text: text,
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(new LeakRefusal(Subject: subject, Rule: LeakRule.PRIVATE_REPOSITORY, LineNumber: lineNumber));
    }

    // A usage error is reported before the leak checks, the board and any GitHub request
    private async Task<ParameterValidationException> AssertRejectedAsync(
        string repository = REPOSITORY,
        string priority = "Medium",
        string title = TITLE,
        string body = BODY,
        IReadOnlyList<string>? labels = null
    )
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.CreateAsync(
                upstream: upstream,
                repository: repository,
                priority: priority,
                title: title,
                body: body,
                labels: labels
            )
        );

        Assert.Empty(upstream.Requests);
        this.LeakChecker.DidNotReceive().CheckBody(Arg.Any<string>());
        await this.AssertBoardNotResolvedAsync();

        return exception;
    }

    // A refusal is reported before the board is resolved and before any GitHub request
    private async Task<IssueCreateRefusedException> AssertRefusedAsync(IReadOnlyList<string>? labels = null)
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        IssueCreateRefusedException exception = await Assert.ThrowsAsync<IssueCreateRefusedException>(() =>
            this.CreateAsync(upstream: upstream, labels: labels)
        );

        Assert.Empty(upstream.Requests);
        await this.AssertBoardNotResolvedAsync();

        return exception;
    }

    private Task AssertBoardNotResolvedAsync()
    {
        return this
            .BoardResolver.DidNotReceive()
            .ResolveAsync(repository: Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>())
            .AsTask();
    }
}
