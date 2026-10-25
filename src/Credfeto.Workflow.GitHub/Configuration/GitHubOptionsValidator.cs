using System;
using Credfeto.Workflow.GitHub.Http;
using Microsoft.Extensions.Options;

namespace Credfeto.Workflow.GitHub.Configuration;

public sealed class GitHubOptionsValidator : IValidateOptions<GitHubOptions>
{
    public ValidateOptionsResult Validate(string? name, GitHubOptions options)
    {
        Uri? apiBaseUrl = options.ApiBaseUrl;

        if (apiBaseUrl is null)
        {
            return ValidateOptionsResult.Fail("GitHub:ApiBaseUrl must be configured.");
        }

        if (!apiBaseUrl.IsAbsoluteUri)
        {
            return ValidateOptionsResult.Fail("GitHub:ApiBaseUrl must be an absolute URL.");
        }

        if (!UriSchemes.IsHttp(apiBaseUrl))
        {
            return ValidateOptionsResult.Fail("GitHub:ApiBaseUrl must use the http or https scheme.");
        }

        // Every request carries the caller's token, so plain http is only safe when it never leaves the machine
        if (!UriSchemes.IsHttps(apiBaseUrl) && !apiBaseUrl.IsLoopback)
        {
            return ValidateOptionsResult.Fail(
                "GitHub:ApiBaseUrl must use https unless the host is loopback (localhost, 127.0.0.0/8 or ::1), because every request carries the caller's token."
            );
        }

        // Every request is checked against the same rule, so a base URL that breaks it would fail every call
        if (UriHosts.HasEncodedPathTraversal(apiBaseUrl))
        {
            return ValidateOptionsResult.Fail(
                "GitHub:ApiBaseUrl path must not contain segments that decode or normalise to '.' or '..', or escapes that cannot be decoded."
            );
        }

        // Relative request URIs only keep the base path when it ends in '/'; a host-only URL has the path '/'
        return IsDirectoryUrl(apiBaseUrl)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("GitHub:ApiBaseUrl path must end in '/' and have no query or fragment.");
    }

    private static bool IsDirectoryUrl(Uri apiBaseUrl)
    {
        return string.IsNullOrEmpty(apiBaseUrl.Query)
            && string.IsNullOrEmpty(apiBaseUrl.Fragment)
            && apiBaseUrl.AbsolutePath is [.., '/'];
    }
}
