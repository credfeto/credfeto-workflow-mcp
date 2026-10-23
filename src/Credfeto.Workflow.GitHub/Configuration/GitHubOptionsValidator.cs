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

        // Every request is checked against the same rule, so a base URL that breaks it would fail every call
        if (UriHosts.HasEncodedPathTraversal(apiBaseUrl))
        {
            return ValidateOptionsResult.Fail(
                "GitHub:ApiBaseUrl path must not contain encoded '.' or '..' segments (such as %2f..%2f)."
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
