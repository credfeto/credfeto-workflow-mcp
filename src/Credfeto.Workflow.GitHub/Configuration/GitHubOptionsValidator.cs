using System;
using System.Linq;
using Credfeto.Workflow.GitHub.Http;
using Microsoft.Extensions.Options;

namespace Credfeto.Workflow.GitHub.Configuration;

public sealed class GitHubOptionsValidator : IValidateOptions<GitHubOptions>
{
    public ValidateOptionsResult Validate(string? name, GitHubOptions options)
    {
        // The version is sent as a header on every request, so a value the handler cannot send would fail every call
        if (!IsHeaderSafe(options.ApiVersion))
        {
            return ValidateOptionsResult.Fail(
                "GitHub:ApiVersion must only contain printable ASCII characters, with no control characters or line breaks."
            );
        }

        ValidateOptionsResult apiBaseUrlResult = ValidateApiBaseUrl(options.ApiBaseUrl);

        return apiBaseUrlResult.Failed ? apiBaseUrlResult : ValidateUpstreamBasePath(options.UpstreamBasePath);
    }

    private static ValidateOptionsResult ValidateUpstreamBasePath(string? upstreamBasePath)
    {
        if (upstreamBasePath is null)
        {
            return ValidateOptionsResult.Success;
        }

        // Parsing and reading the path back must give the same text, so a value that is not already
        // percent-encoded, carries a query or fragment, or would be normalised (a literal dot segment) never
        // reaches the prefix match on next page links, where it would silently never match
        if (
            upstreamBasePath is not (['/'] or ['/', .., '/'])
            || !UpstreamBasePaths.TryCreate(upstreamBasePath: upstreamBasePath, out Uri? upstream)
            || !StringComparer.Ordinal.Equals(x: upstream.AbsolutePath, y: upstreamBasePath)
        )
        {
            return ValidateOptionsResult.Fail(
                "GitHub:UpstreamBasePath must be a URL path that starts and ends in '/', is already percent-encoded, and has no query or fragment."
            );
        }

        // The derived GraphQL endpoint is built from this path, so it must obey the same rule as every request
        return UriHosts.HasEncodedPathTraversal(upstream)
            ? ValidateOptionsResult.Fail(
                "GitHub:UpstreamBasePath must not contain segments that decode or normalise to '.' or '..', or escapes that cannot be decoded."
            )
            : ValidateOptionsResult.Success;
    }

    private static bool IsHeaderSafe(string? apiVersion)
    {
        return apiVersion is null || apiVersion.All(static c => c is >= ' ' and <= '~');
    }

    private static ValidateOptionsResult ValidateApiBaseUrl(Uri? apiBaseUrl)
    {
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
