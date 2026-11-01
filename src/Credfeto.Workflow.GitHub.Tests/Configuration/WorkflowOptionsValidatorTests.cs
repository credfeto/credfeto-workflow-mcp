using System;
using System.Collections.Generic;
using Credfeto.Workflow.GitHub.Configuration;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using FunFair.Test.Common;
using Microsoft.Extensions.Options;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Configuration;

public sealed class WorkflowOptionsValidatorTests : TestBase
{
    private readonly WorkflowOptionsValidator _validator = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("credfeto")]
    [InlineData("credfeto funfair-tech")]
    [InlineData("  credfeto   a-b1  ")]
    public void ValidPrivateOwnersSucceeds(string? privateOwners)
    {
        ValidateOptionsResult result = this.Validate(privateOwners);

        Assert.True(condition: result.Succeeded, userMessage: result.FailureMessage);
    }

    [Theory]
    [InlineData("-credfeto")]
    [InlineData("cred_feto")]
    [InlineData("credfeto/repo")]
    [InlineData("credfeto,funfair")]
    [InlineData("credfeto\tfunfair")]
    public void InvalidPrivateOwnersFails(string privateOwners)
    {
        ValidateOptionsResult result = this.Validate(privateOwners);

        Assert.True(condition: result.Failed, userMessage: "Validation should fail for " + privateOwners);
        Assert.Equal(
            expected: "Workflow:PrivateOwners must be a space-separated list of GitHub owner names; each may only contain letters, digits and '-' and must not start with '-'.",
            actual: result.FailureMessage
        );
    }

    [Fact]
    public static void ParsePrivateOwnersReturnsEachOwner()
    {
        IReadOnlyList<string> owners = WorkflowOptionsValidator.ParsePrivateOwners("  credfeto   a-b1  ");

        Assert.Equal(expected: ["credfeto", "a-b1"], actual: owners, comparer: StringComparer.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public static void ParsePrivateOwnersReturnsEmptyWhenUnset(string? privateOwners)
    {
        Assert.Empty(WorkflowOptionsValidator.ParsePrivateOwners(privateOwners));
    }

    [Fact]
    public static void ParsePrivateOwnersRejectsAnInvalidOwner()
    {
        _ = Assert.Throws<ParameterValidationException>(() => WorkflowOptionsValidator.ParsePrivateOwners("ok -bad"));
    }

    private ValidateOptionsResult Validate(string? privateOwners)
    {
        return this._validator.Validate(name: null, options: new WorkflowOptions { PrivateOwners = privateOwners });
    }
}
