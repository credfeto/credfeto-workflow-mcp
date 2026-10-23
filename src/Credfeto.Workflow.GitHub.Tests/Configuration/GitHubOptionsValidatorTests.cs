using System;
using Credfeto.Workflow.GitHub.Configuration;
using FunFair.Test.Common;
using Microsoft.Extensions.Options;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Configuration;

public sealed class GitHubOptionsValidatorTests : TestBase
{
    private readonly GitHubOptionsValidator _validator = new();

    [Theory]
    [InlineData("https://api.github.com/")]
    [InlineData("http://github-proxy.example/")]
    [InlineData("https://github.example/api/v3/")]
    [InlineData("https://api.github.com")]
    [InlineData("https://proxy.example/a%2Fb/")]
    [InlineData("https://proxy.example/a%5Cb/")]
    public void ValidApiBaseUrlSucceeds(string apiBaseUrl)
    {
        ValidateOptionsResult result = this.Validate(new Uri(apiBaseUrl));

        Assert.True(condition: result.Succeeded, userMessage: result.FailureMessage);
    }

    [Theory]
    [InlineData("api/")]
    [InlineData("ftp://api.github.com/")]
    [InlineData("https://github.example/api/v3")]
    [InlineData("https://api.github.com/?page=/")]
    [InlineData("https://api.github.com/#fragment/")]
    [InlineData(null)]
    public void InvalidApiBaseUrlFails(string? apiBaseUrl)
    {
        ValidateOptionsResult result = this.Validate(
            apiBaseUrl is null ? null : new Uri(uriString: apiBaseUrl, uriKind: UriKind.RelativeOrAbsolute)
        );

        Assert.True(
            condition: result.Failed,
            userMessage: "Validation should fail for " + (apiBaseUrl ?? "a missing ApiBaseUrl")
        );
    }

    [Theory]
    [InlineData("https://proxy.example/a%2f..%2fb/")]
    [InlineData("https://proxy.example/a%5C..%5Cb/")]
    [InlineData("https://proxy.example/%252e%252e/b/")]
    public void TraversingApiBaseUrlFails(string apiBaseUrl)
    {
        ValidateOptionsResult result = this.Validate(new Uri(apiBaseUrl));

        Assert.Equal(
            expected: "GitHub:ApiBaseUrl path must not contain encoded '.' or '..' segments (such as %2f..%2f).",
            actual: result.FailureMessage
        );
    }

    private ValidateOptionsResult Validate(Uri? apiBaseUrl)
    {
        return this._validator.Validate(name: null, options: new GitHubOptions { ApiBaseUrl = apiBaseUrl });
    }
}
