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
    public void ValidApiBaseUrlSucceeds(string apiBaseUrl)
    {
        ValidateOptionsResult result = this.Validate(new Uri(apiBaseUrl));

        Assert.True(condition: result.Succeeded, userMessage: result.FailureMessage);
    }

    [Theory]
    [InlineData("api/")]
    [InlineData("ftp://api.github.com/")]
    [InlineData("https://api.github.com")]
    [InlineData("https://github.example/api/v3")]
    [InlineData("https://api.github.com/?page=/")]
    [InlineData("https://api.github.com/#fragment/")]
    public void InvalidApiBaseUrlFails(string apiBaseUrl)
    {
        ValidateOptionsResult result = this.Validate(
            new Uri(uriString: apiBaseUrl, uriKind: UriKind.RelativeOrAbsolute)
        );

        Assert.True(condition: result.Failed, userMessage: "Validation should fail for " + apiBaseUrl);
    }

    [Fact]
    public void MissingApiBaseUrlFails()
    {
        ValidateOptionsResult result = this.Validate(null);

        Assert.True(condition: result.Failed, userMessage: "Validation should fail when ApiBaseUrl is missing");
    }

    private ValidateOptionsResult Validate(Uri? apiBaseUrl)
    {
        return this._validator.Validate(name: null, options: new GitHubOptions { ApiBaseUrl = apiBaseUrl });
    }
}
