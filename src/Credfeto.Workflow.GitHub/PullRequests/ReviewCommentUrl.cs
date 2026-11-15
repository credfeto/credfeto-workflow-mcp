using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Credfeto.Workflow.GitHub.Http;

namespace Credfeto.Workflow.GitHub.PullRequests;

// The link GitHub answers for an inline review comment, accepted only when it names the expected pull request and comment
internal static partial class ReviewCommentUrl
{
    private const int REGEX_TIMEOUT_MILLISECONDS = 1000;

    public static Uri? Parse(string? htmlUrl, string validRepository, int pullRequest, long id)
    {
        if (!Uri.TryCreate(uriString: htmlUrl, uriKind: UriKind.Absolute, out Uri? uri) || !UriSchemes.IsHttp(uri))
        {
            return null;
        }

        Match match = UrlPattern().Match(htmlUrl);

        return
            match.Success
            && StringComparer.OrdinalIgnoreCase.Equals(x: match.Groups["repo"].Value, y: validRepository)
            && StringComparer.Ordinal.Equals(
                x: match.Groups["pr"].Value,
                y: pullRequest.ToString(CultureInfo.InvariantCulture)
            )
            && StringComparer.Ordinal.Equals(x: match.Groups["id"].Value, y: id.ToString(CultureInfo.InvariantCulture))
            ? uri
            : null;
    }

    [GeneratedRegex(
        pattern: "^https?://[A-Za-z0-9.-]+(:[0-9]+)?/(?<repo>[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+)/pull/(?<pr>[0-9]+)#discussion_r(?<id>[0-9]+)$",
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: REGEX_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex UrlPattern();
}
