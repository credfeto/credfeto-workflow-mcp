using System;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Configuration;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Leaks;
using FunFair.Test.Common;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.Core;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Leaks;

// Every fake credential is built from fragments so this file is not itself flagged by secret scanners or push
// protection. The real leak checker is used, so each refusal is the one the rules give
public sealed class BodyCheckServiceTests : TestBase
{
    private const string TARGET = "credfeto/public-repo";

    private const string SECRET = "gh" + "p_abcdef";

    private const string EMPTY_BODY = "the body is empty";

    private readonly IBodyCheckService _service;
    private readonly IRepositoryVisibilityLookup _visibilityLookup;

    public BodyCheckServiceTests()
    {
        this._visibilityLookup = GetSubstitute<IRepositoryVisibilityLookup>();
        this._service = new BodyCheckService(
            new LeakChecker(
                visibilityLookup: this._visibilityLookup,
                options: Options.Create(new WorkflowOptions { PrivateOwners = "secretorg" })
            )
        );
    }

    [Fact]
    public async Task CleanBodyPassesWithoutALookupAsync()
    {
        BodyCheckResult result = await this.CheckAsync("## Summary\n\nNothing to see\twith a tab.");

        Assert.Equal(
            expected: new BodyCheckResult(Outcome: BodyCheckOutcome.Passed, Rule: null, Line: null, Message: null),
            actual: result
        );
        await this.ReceivedNoLookupsAsync();
    }

    [Theory]
    [InlineData("Approved")]
    [InlineData("lgtm, approved")]
    public async Task ApprovalKeywordsAreNotRefusedAsync(string body)
    {
        BodyCheckResult result = await this.CheckAsync(body);

        Assert.Equal(expected: BodyCheckOutcome.Passed, actual: result.Outcome);
    }

    [Fact]
    public async Task TokenIsRefusedWithItsLineAsync()
    {
        BodyCheckResult result = await this.CheckAsync("line one\ntoken " + SECRET + "\nline three");

        AssertRefused(result: result, rule: "token", line: 2);
        Assert.DoesNotContain(
            expectedSubstring: SECRET,
            actualString: result.ToString(),
            comparisonType: StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task HostPathIsRefusedWithItsLineAsync()
    {
        BodyCheckResult result = await this.CheckAsync("ok\nok\nsee /tmp/x");

        AssertRefused(result: result, rule: "host path", line: 3);
        Assert.DoesNotContain(
            expectedSubstring: "/tmp/x",
            actualString: result.ToString(),
            comparisonType: StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task ControlCharacterIsRefusedWithItsLineAsync()
    {
        BodyCheckResult result = await this.CheckAsync("a\nb\r\nc");

        AssertRefused(result: result, rule: "control character", line: 2);
    }

    [Fact]
    public async Task InvalidUtf8IsRefusedWithItsLineAsync()
    {
        BodyCheckResult result = await this.CheckAsync("ok\nbad " + (char)0xD800);

        AssertRefused(result: result, rule: "invalid UTF-8", line: 2);
    }

    [Fact]
    public async Task TooLongBodyIsRefusedWithoutALineAsync()
    {
        BodyCheckResult result = await this.CheckAsync(new('a', count: LeakPatterns.MAXIMUM_BODY_LENGTH + 1));

        Assert.Equal(
            expected: new BodyCheckResult(
                Outcome: BodyCheckOutcome.Refused,
                Rule: "too long",
                Line: null,
                Message: "body refused: too long"
            ),
            actual: result
        );
    }

    [Fact]
    public async Task PrivateRepositoryIsRefusedWithItsLineAsync()
    {
        this.GivenVisibility(repository: TARGET, visibility: RepositoryVisibility.PUBLIC);

        BodyCheckResult result = await this.CheckAsync("line one\nsee SecretOrg/hidden#3");

        AssertRefused(result: result, rule: "private repository", line: 2);
        Assert.DoesNotContain(
            expectedSubstring: "hidden",
            actualString: result.ToString(),
            comparisonType: StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public async Task PrivateRepositoryIsAllowedInANonPublicTargetAsync()
    {
        this.GivenVisibility(repository: TARGET, visibility: RepositoryVisibility.PRIVATE);

        BodyCheckResult result = await this.CheckAsync("see secretorg/hidden#3");

        Assert.Equal(expected: BodyCheckOutcome.Passed, actual: result.Outcome);
    }

    [Fact]
    public async Task TextRulesAreAppliedBeforeTheRepositoryRuleAsync()
    {
        BodyCheckResult result = await this.CheckAsync("see secretorg/hidden#3\nsee /tmp/x");

        AssertRefused(result: result, rule: "host path", line: 2);
        await this.ReceivedNoLookupsAsync();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" \t\n\v\f\r\n")]
    [InlineData("\u3000")]
    [InlineData("\u200B\u00A0 \n\uFEFF")]
    [InlineData("\U000E0020\u3164")]
    public async Task WhitespaceOrInvisibleBodyIsEmptyAsync(string body)
    {
        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.CheckAsync(body)
        );

        AssertEmptyBody(exception);
    }

    [Fact]
    public async Task InvisibleBodyWithAControlCharacterIsRefusedRatherThanEmptyAsync()
    {
        BodyCheckResult result = await this.CheckAsync("\u3000\r");

        AssertRefused(result: result, rule: "control character", line: 1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("credfeto")]
    [InlineData("credfeto/a/b")]
    [InlineData("credfeto/..")]
    public async Task InvalidRepositoryIsAUsageErrorAsync(string repository)
    {
        ParameterValidationException exception = await Assert.ThrowsAsync<ParameterValidationException>(() =>
            this.CheckAsync(body: "see secretorg/hidden#3", repository: repository)
        );

        Assert.Equal(expected: "repository", actual: exception.ParameterName);
        await this.ReceivedNoLookupsAsync();
    }

    [Fact]
    public async Task MissingTargetIsAFailureAsync()
    {
        this.GivenVisibility(repository: TARGET, visibility: RepositoryVisibility.NOT_FOUND);

        _ = await Assert.ThrowsAsync<RepositoryNotFoundException>(() => this.CheckAsync("see credfeto/other#1"));
    }

    [Fact]
    public async Task VisibilityLookupFailureIsAFailureAsync()
    {
        this.GivenVisibility(repository: TARGET, visibility: RepositoryVisibility.PUBLIC);
        this.GivenLookup(
            repository: "credfeto/other",
            result: _ =>
                ValueTask.FromException<RepositoryVisibility>(
                    new RepositoryVisibilityLookupFailedException(
                        message: "the visibility of a referenced repository could not be read",
                        innerException: new TimeoutException()
                    )
                )
        );

        _ = await Assert.ThrowsAsync<RepositoryVisibilityLookupFailedException>(() =>
            this.CheckAsync("see credfeto/other#1")
        );
    }

    private static void AssertRefused(BodyCheckResult result, string rule, int line)
    {
        Assert.Equal(
            expected: new BodyCheckResult(
                Outcome: BodyCheckOutcome.Refused,
                Rule: rule,
                Line: line,
                Message: $"body refused: {rule} (line {line})"
            ),
            actual: result
        );
    }

    private static void AssertEmptyBody(ParameterValidationException exception)
    {
        Assert.Equal(expected: EMPTY_BODY, actual: exception.Message);
        Assert.Equal(expected: "body", actual: exception.ParameterName);
        Assert.Equal(expected: EMPTY_BODY, actual: exception.Rule);
    }

    private void GivenVisibility(string repository, RepositoryVisibility visibility)
    {
        this.GivenLookup(repository: repository, result: _ => ValueTask.FromResult(visibility));
    }

    private void GivenLookup(string repository, Func<CallInfo, ValueTask<RepositoryVisibility>> result)
    {
        Configure(
            call: this._visibilityLookup.GetVisibilityAsync(
                repository: repository,
                cancellationToken: Arg.Any<CancellationToken>()
            ),
            result: result
        );
    }

    private static void Configure(
        ValueTask<RepositoryVisibility> call,
        Func<CallInfo, ValueTask<RepositoryVisibility>> result
    )
    {
        _ = call.Returns(result);
    }

    private Task<BodyCheckResult> CheckAsync(string body, string repository = TARGET)
    {
        return this
            ._service.CheckAsync(repository: repository, body: body, cancellationToken: this.CancellationToken())
            .AsTask();
    }

    private ValueTask<RepositoryVisibility> ReceivedNoLookupsAsync()
    {
        return this
            ._visibilityLookup.DidNotReceive()
            .GetVisibilityAsync(repository: Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>());
    }
}
