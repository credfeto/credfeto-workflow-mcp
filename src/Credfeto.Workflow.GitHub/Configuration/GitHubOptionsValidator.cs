using System;
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

        if (!IsHttp(apiBaseUrl))
        {
            return ValidateOptionsResult.Fail("GitHub:ApiBaseUrl must use the http or https scheme.");
        }

        // Relative request URIs only keep the base path when the base address ends in '/'
        return IsDirectoryUrl(apiBaseUrl)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("GitHub:ApiBaseUrl must end in '/' and have no query or fragment.");
    }

    private static bool IsHttp(Uri apiBaseUrl)
    {
        return StringComparer.OrdinalIgnoreCase.Equals(x: apiBaseUrl.Scheme, y: Uri.UriSchemeHttps)
            || StringComparer.OrdinalIgnoreCase.Equals(x: apiBaseUrl.Scheme, y: Uri.UriSchemeHttp);
    }

    private static bool IsDirectoryUrl(Uri apiBaseUrl)
    {
        return string.IsNullOrEmpty(apiBaseUrl.Query)
            && string.IsNullOrEmpty(apiBaseUrl.Fragment)
            && apiBaseUrl.OriginalString[^1] == '/';
    }
}
