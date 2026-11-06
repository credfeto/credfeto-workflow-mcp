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
    [InlineData("http://localhost:8080/")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://[::1]/")]
    [InlineData("https://github.example/api/v3/")]
    [InlineData("https://api.github.com")]
    [InlineData("https://proxy.example/a%2Fb/")]
    [InlineData("https://proxy.example/a%5Cb/")]
    [InlineData("https://proxy.example/a/.../b/")]
    [InlineData("https://proxy.example/a/.github/b/")]
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
    [InlineData("https://proxy.example/a/..;/b/")]
    [InlineData("https://proxy.example/a/..%3F/b/")]
    [InlineData("https://proxy.example/a/..%00/b/")]
    [InlineData("https://proxy.example/a/..%20/b/")]
    [InlineData("https://proxy.example/a/..%09/b/")]
    [InlineData("https://proxy.example/a/%20..%20/b/")]
    [InlineData("https://proxy.example/a/%c0%ae%c0%ae/b/")]
    public void TraversingApiBaseUrlFails(string apiBaseUrl)
    {
        ValidateOptionsResult result = this.Validate(new Uri(apiBaseUrl));

        Assert.Equal(
            expected: "GitHub:ApiBaseUrl path must not contain segments that decode or normalise to '.' or '..', or escapes that cannot be decoded.",
            actual: result.FailureMessage
        );
    }

    [Theory]
    [InlineData("http://github-proxy.internal/")]
    [InlineData("http://github-proxy.example/api/v3/")]
    [InlineData("http://10.0.0.1/")]
    public void PlainHttpNonLoopbackApiBaseUrlFails(string apiBaseUrl)
    {
        ValidateOptionsResult result = this.Validate(new Uri(apiBaseUrl));

        Assert.Equal(
            expected: "GitHub:ApiBaseUrl must use https unless the host is loopback (localhost, 127.0.0.0/8 or ::1), because every request carries the caller's token.",
            actual: result.FailureMessage
        );
    }

    [Theory]
    [InlineData("2026-03-10")]
    [InlineData("2022-11-28")]
    [InlineData("ghes-3.14 preview")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ValidApiVersionSucceeds(string? apiVersion)
    {
        ValidateOptionsResult result = this.ValidateApiVersion(apiVersion);

        Assert.True(condition: result.Succeeded, userMessage: result.FailureMessage);
    }

    [Theory]
    [InlineData("2026-03-10\r\nX-Injected: 1")]
    [InlineData("2026-03-10\n")]
    [InlineData("2026-03-10\r")]
    [InlineData("2026\t03-10")]
    [InlineData("2026-03-10\0")]
    [InlineData("2026-03-10\u007f")]
    [InlineData("2026-03-10\u0085")]
    [InlineData("2026-03-10\x2028")]
    [InlineData("2026-03-10\xe9")]
    public void HeaderUnsafeApiVersionFails(string apiVersion)
    {
        ValidateOptionsResult result = this.ValidateApiVersion(apiVersion);

        Assert.Equal(
            expected: "GitHub:ApiVersion must only contain printable ASCII characters, with no control characters or line breaks.",
            actual: result.FailureMessage
        );
    }

    private ValidateOptionsResult ValidateApiVersion(string? apiVersion)
    {
        return this._validator.Validate(
            name: null,
            options: new GitHubOptions { ApiBaseUrl = new Uri("https://api.github.com/"), ApiVersion = apiVersion }
        );
    }

    private ValidateOptionsResult Validate(Uri? apiBaseUrl)
    {
        return this._validator.Validate(name: null, options: new GitHubOptions { ApiBaseUrl = apiBaseUrl });
    }
}
