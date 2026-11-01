using System;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Configuration;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Leaks;
using FunFair.Test.Common;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.Core;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Leaks;

public sealed class LeakCheckerRepositoryReferenceTests : TestBase
{
    private const string TARGET = "credfeto/public-repo";

    private readonly ILeakChecker _leakChecker;
    private readonly IRepositoryVisibilityLookup _visibilityLookup;

    public LeakCheckerRepositoryReferenceTests()
    {
        this._visibilityLookup = GetSubstitute<IRepositoryVisibilityLookup>();
        this._leakChecker = new LeakChecker(
            visibilityLookup: this._visibilityLookup,
            options: Options.Create(new WorkflowOptions { PrivateOwners = "secretorg" })
        );
    }

    [Theory]
    [InlineData("no references here")]
    [InlineData("see https://github.com/credfeto/public-repo and Credfeto/Public-Repo#1")]
    public async Task TextWithoutOtherReferencesNeedsNoLookupAsync(string text)
    {
        LeakRefusal? refusal = await this.CheckAsync(text);

        Assert.Null(refusal);
        await this.ReceivedNoLookupsAsync();
    }

    [Fact]
    public async Task MissingTargetFailsWithoutNamingItAsync()
    {
        this.GivenVisibility(repository: TARGET, visibility: RepositoryVisibility.NOT_FOUND);

        RepositoryNotFoundException exception = await Assert.ThrowsAsync<RepositoryNotFoundException>(() =>
            this.CheckAsync("see credfeto/other#1").AsTask()
        );

        Assert.DoesNotContain(
            expectedSubstring: "public-repo",
            actualString: exception.Message,
            comparisonType: StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData(RepositoryVisibility.PRIVATE)]
    [InlineData(RepositoryVisibility.INTERNAL)]
    public async Task NonPublicTargetAllowsAnyReferenceAsync(RepositoryVisibility visibility)
    {
        this.GivenVisibility(repository: TARGET, visibility: visibility);

        LeakRefusal? refusal = await this.CheckAsync("see secretorg/hidden and credfeto/other#1");

        Assert.Null(refusal);
        await this.ReceivedLookupsAsync(repository: "credfeto/other", count: 0);
    }

    [Fact]
    public async Task PrivateOwnerReferenceIsRefusedWithoutLookupAsync()
    {
        this.GivenVisibility(repository: TARGET, visibility: RepositoryVisibility.PUBLIC);

        LeakRefusal? refusal = await this.CheckAsync("line one\nsee SecretOrg/hidden");

        Assert.Equal(
            expected: new LeakRefusal(Subject: LeakSubject.BODY, Rule: LeakRule.PRIVATE_REPOSITORY, LineNumber: 2),
            actual: refusal
        );
        await this.ReceivedLookupsAsync(repository: "SecretOrg/hidden", count: 0);
    }

    [Fact]
    public async Task PublicReferencePassesAsync()
    {
        this.GivenVisibility(repository: TARGET, visibility: RepositoryVisibility.PUBLIC);
        this.GivenVisibility(repository: "credfeto/other", visibility: RepositoryVisibility.PUBLIC);

        LeakRefusal? refusal = await this.CheckAsync("see https://github.com/credfeto/other");

        Assert.Null(refusal);
    }

    [Theory]
    [InlineData(RepositoryVisibility.PRIVATE)]
    [InlineData(RepositoryVisibility.INTERNAL)]
    [InlineData(RepositoryVisibility.NOT_FOUND)]
    public async Task NonPublicReferenceIsRefusedWithItsLineAsync(RepositoryVisibility visibility)
    {
        this.GivenVisibility(repository: TARGET, visibility: RepositoryVisibility.PUBLIC);
        this.GivenVisibility(repository: "credfeto/public", visibility: RepositoryVisibility.PUBLIC);
        this.GivenVisibility(repository: "credfeto/hidden", visibility: visibility);

        LeakRefusal? refusal = await this.CheckAsync(
            "fixes credfeto/public#1\n\nand credfeto/hidden#2",
            subject: LeakSubject.TITLE
        );

        Assert.Equal(
            expected: new LeakRefusal(Subject: LeakSubject.TITLE, Rule: LeakRule.PRIVATE_REPOSITORY, LineNumber: 3),
            actual: refusal
        );
    }

    [Fact]
    public async Task RepeatedReferenceIsLookedUpOnceAsync()
    {
        this.GivenVisibility(repository: TARGET, visibility: RepositoryVisibility.PUBLIC);
        this.GivenVisibility(repository: "credfeto/other", visibility: RepositoryVisibility.PUBLIC);

        LeakRefusal? refusal = await this.CheckAsync(
            "credfeto/other#1\nCredfeto/Other#2\nhttps://github.com/credfeto/other/pull/3"
        );

        Assert.Null(refusal);
        await this.ReceivedLookupsAsync(repository: "credfeto/other", count: 1);
    }

    [Fact]
    public async Task LookupFailurePropagatesAsync()
    {
        this.GivenVisibility(repository: TARGET, visibility: RepositoryVisibility.PUBLIC);
        this.GivenLookup(
            repository: "credfeto/other",
            result: _ =>
                ValueTask.FromException<RepositoryVisibility>(
                    new RepositoryVisibilityLookupFailedException(
                        message: "failed",
                        innerException: new TimeoutException()
                    )
                )
        );

        _ = await Assert.ThrowsAsync<RepositoryVisibilityLookupFailedException>(() =>
            this.CheckAsync("see credfeto/other#1").AsTask()
        );
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

    private ValueTask<LeakRefusal?> CheckAsync(string text, LeakSubject subject = LeakSubject.BODY)
    {
        return this._leakChecker.CheckRepositoryReferencesAsync(
            repository: TARGET,
            subject: subject,
            text: text,
            cancellationToken: this.CancellationToken()
        );
    }

    private ValueTask<RepositoryVisibility> ReceivedLookupsAsync(string repository, int count)
    {
        return this
            ._visibilityLookup.Received(count)
            .GetVisibilityAsync(repository: repository, cancellationToken: Arg.Any<CancellationToken>());
    }

    private ValueTask<RepositoryVisibility> ReceivedNoLookupsAsync()
    {
        return this
            ._visibilityLookup.DidNotReceive()
            .GetVisibilityAsync(repository: Arg.Any<string>(), cancellationToken: Arg.Any<CancellationToken>());
    }
}
