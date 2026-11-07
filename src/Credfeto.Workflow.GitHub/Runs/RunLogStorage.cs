using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;

namespace Credfeto.Workflow.GitHub.Runs;

// GitHub answers a log request with a short-lived signed URL on its log storage, which already authorises the read,
// so the caller's token is never sent there and only GitHub's own storage hosts are read
public static partial class RunLogStorage
{
    public const string RESULTS_RECEIVER_HOST = "results-receiver.actions.githubusercontent.com";

    private const string BLOB_STORAGE_HOST = @"^productionresultssa[0-9]+\.blob\.core\.windows\.net$";

    private const int REGEX_TIMEOUT_MILLISECONDS = 1000;

    // Only the host is named on a refusal: the rest of a signed URL is its credential
    public static Uri Validate(Uri location)
    {
        if (!location.IsAbsoluteUri)
        {
            throw new RunLogFailedException("GitHub returned a log location that is not absolute");
        }

        if (
            !StringComparer.Ordinal.Equals(x: location.Scheme, y: Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(location.UserInfo)
            || !location.IsDefaultPort
            || !IsStorageHost(location.IdnHost)
        )
        {
            throw new RunLogFailedException($"GitHub returned a log location on an unexpected host {location.IdnHost}");
        }

        return location;
    }

    public static async ValueTask<byte[]> DownloadAsync(
        IHttpClientFactory httpClientFactory,
        Uri location,
        CancellationToken cancellationToken
    )
    {
        HttpClient httpClient = httpClientFactory.CreateClient(GitHubSetup.LogStorageHttpClientName);

        using HttpRequestMessage request = new(method: HttpMethod.Get, requestUri: Validate(location));

        using HttpResponseMessage response = await GitHubHttpSender.SendAsync(
            httpClient: httpClient,
            request: request,
            cancellationToken: cancellationToken
        );

        // Redirects are never followed, so a further hop cannot lead the read off the checked host
        if (!response.IsSuccessStatusCode)
        {
            throw new RunLogFailedException($"the log storage answered {(int)response.StatusCode} for the run's logs");
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    // Uri gives the host in lower case, so an exact match is enough
    private static bool IsStorageHost(string host)
    {
        return StringComparer.Ordinal.Equals(x: host, y: RESULTS_RECEIVER_HOST) || BlobStorageHost().IsMatch(host);
    }

    [GeneratedRegex(
        pattern: BLOB_STORAGE_HOST,
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: REGEX_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex BlobStorageHost();
}
