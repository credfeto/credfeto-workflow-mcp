using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Validation;
using NSubstitute;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.PullRequests;

public sealed class PrCreateServiceValidationTests : PrCreateServiceTestBase
{
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
    [InlineData("-x")]
    [InlineData("a..b")]
    [InlineData("a b")]
    public async Task AnInvalidHeadIsRejectedAsync(string head)
    {
        ParameterValidationException exception = await this.AssertRejectedAsync(head: head);

        Assert.Equal(expected: "head", actual: exception.ParameterName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-x")]
    [InlineData("a..b")]
    [InlineData("a b")]
    public async Task AnInvalidBaseIsRejectedAsync(string baseBranch)
    {
        ParameterValidationException exception = await this.AssertRejectedAsync(baseBranch: baseBranch);

        Assert.Equal(expected: "baseBranch", actual: exception.ParameterName);
    }

    [Fact]
    public async Task ABaseThatIsTheHeadIsRejectedAsync()
    {
        ParameterValidationException exception = await this.AssertRejectedAsync(baseBranch: HEAD);

        Assert.Equal(expected: "--base and --head must be different branches", actual: exception.Message);
        Assert.Equal(expected: "baseBranch", actual: exception.ParameterName);
        Assert.Equal(expected: "--base and --head must be different branches", actual: exception.Rule);
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
    [InlineData("Add a pr create tool")]
    [InlineData("Feat: add a pr create tool")]
    [InlineData("feat:add a pr create tool")]
    [InlineData("feat:  add a pr create tool")]
    public async Task ATitleThatIsNotAConventionalCommitsTitleIsRejectedAsync(string title)
    {
        ParameterValidationException exception = await this.AssertRejectedAsync(title: title);

        Assert.Equal(expected: TitleValidator.INVALID_PULL_REQUEST_TITLE_MESSAGE, actual: exception.Message);
        Assert.Equal(expected: "title", actual: exception.ParameterName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \n\t ")]
    [InlineData("​ ")]
    public async Task ABlankBodyIsRejectedAsync(string body)
    {
        ParameterValidationException exception = await this.AssertRejectedAsync(body: body);

        Assert.Equal(expected: "the body is empty", actual: exception.Message);
        Assert.Equal(expected: "body", actual: exception.ParameterName);
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

        PrCreateRefusedException exception = await this.AssertRefusedAsync();

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

        PrCreateRefusedException exception = await this.AssertRefusedAsync();

        Assert.Equal(expected: "body refused: too long", actual: exception.Message);
        Assert.Null(exception.LineNumber);
    }

    [Fact]
    public async Task ABodyNamingAPrivateRepositoryIsRefusedAsync()
    {
        this.ReferencesRefused(subject: LeakSubject.BODY, text: BODY, lineNumber: 3);

        PrCreateRefusedException exception = await this.AssertRefusedAsync();

        Assert.Equal(expected: "body refused: private repository (line 3)", actual: exception.Message);
    }

    [Fact]
    public async Task ARefusedTitleNamesOnlyTheRuleAsync()
    {
        this.LeakChecker.CheckValue(subject: LeakSubject.TITLE, value: TITLE)
            .Returns(new LeakRefusal(Subject: LeakSubject.TITLE, Rule: LeakRule.HOST_PATH, LineNumber: 1));

        PrCreateRefusedException exception = await this.AssertRefusedAsync();

        Assert.Equal(expected: "title refused: host path", actual: exception.Message);
        Assert.Equal(expected: "title", actual: exception.Subject);
        Assert.Equal(expected: "host path", actual: exception.Rule);
        Assert.Null(exception.LineNumber);
    }

    [Fact]
    public async Task ATitleNamingAPrivateRepositoryIsRefusedAsync()
    {
        this.ReferencesRefused(subject: LeakSubject.TITLE, text: TITLE, lineNumber: 1);

        PrCreateRefusedException exception = await this.AssertRefusedAsync();

        Assert.Equal(expected: "title refused: private repository", actual: exception.Message);
    }

    [Fact]
    public async Task TheBodyAndTitleAreLeakCheckedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(CreateFlow);

        await this.CreateAsync(upstream: upstream);

        this.LeakChecker.Received(1).CheckBody(BODY);
        this.LeakChecker.Received(1).CheckValue(subject: LeakSubject.TITLE, value: TITLE);
        await this
            .LeakChecker.Received(1)
            .CheckRepositoryReferencesAsync(
                repository: REPOSITORY,
                subject: LeakSubject.BODY,
                text: BODY,
                cancellationToken: Arg.Any<CancellationToken>()
            );
        await this
            .LeakChecker.Received(1)
            .CheckRepositoryReferencesAsync(
                repository: REPOSITORY,
                subject: LeakSubject.TITLE,
                text: TITLE,
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
        string head = HEAD,
        string? baseBranch = BASE,
        string title = TITLE,
        string body = BODY
    )
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.CreateAsync(
                upstream: upstream,
                repository: repository,
                head: head,
                baseBranch: baseBranch,
                title: title,
                body: body
            )
        );

        Assert.Empty(upstream.Requests);
        this.LeakChecker.DidNotReceive().CheckBody(Arg.Any<string>());
        await this.AssertBoardNotResolvedAsync();

        return exception;
    }

    // A refusal is reported before the board is resolved and before any GitHub request
    private async Task<PrCreateRefusedException> AssertRefusedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(Unexpected);

        PrCreateRefusedException exception = await Assert.ThrowsAsync<PrCreateRefusedException>(() =>
            this.CreateAsync(upstream: upstream)
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
