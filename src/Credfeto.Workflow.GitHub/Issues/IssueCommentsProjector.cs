using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Http;

namespace Credfeto.Workflow.GitHub.Issues;

// Turns upstream comments into the tool's output, refusing any comment with a missing or malformed required field
[DebuggerDisplay("{_reference}")]
internal sealed class IssueCommentsProjector
{
    public const string TIMESTAMP_FORMAT = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    private const int MAX_COMMENTS = 100;

    private const int MAX_BODY_LINES = 500;

    private const string BOT_SUFFIX = "[bot]";

    private readonly string _reference;

    public IssueCommentsProjector(string reference)
    {
        this._reference = reference;
    }

    public static bool TryParseTimestamp(string value, out DateTime timestamp)
    {
        return DateTime.TryParseExact(
            s: value,
            format: TIMESTAMP_FORMAT,
            provider: CultureInfo.InvariantCulture,
            style: DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            result: out timestamp
        );
    }

    public IssueComments Project(
        Uri url,
        IReadOnlyList<IssueCommentResponse?> responses,
        DateTime? since,
        string? author
    )
    {
        IReadOnlyList<IssueComment> matched =
        [
            .. responses
                .Select(response => this.Comment(response ?? throw this.Incomplete()))
                .Where(comment => Matches(comment: comment, since: since, author: author))
                .Select(comment => comment.Comment),
        ];

        // Only the most recent comments are kept when more match than can be returned
        IReadOnlyList<IssueComment> printed =
            matched.Count > MAX_COMMENTS ? [.. matched.Skip(matched.Count - MAX_COMMENTS)] : matched;

        return new(
            Issue: this._reference,
            Url: url,
            Comments: printed,
            Summary: new(
                Printed: printed.Count,
                Matched: matched.Count,
                Total: responses.Count,
                Omitted: matched.Count - printed.Count
            )
        );
    }

    private static bool Matches((IssueComment Comment, DateTime Created) comment, DateTime? since, string? author)
    {
        return (since is null || comment.Created >= since.Value)
            && (author is null || StringComparer.OrdinalIgnoreCase.Equals(x: comment.Comment.Author, y: author));
    }

    private (IssueComment Comment, DateTime Created) Comment(IssueCommentResponse response)
    {
        string createdAt = this.Header(response.CreatedAt);

        if (!TryParseTimestamp(value: createdAt, out DateTime created))
        {
            throw this.Incomplete();
        }

        IReadOnlyList<string> lines = Lines(response.Body ?? throw this.Incomplete());

        return (
            new(
                Id: this.Id(response.Id),
                Author: this.Author(response.User),
                Association: this.Header(response.AuthorAssociation),
                CreatedAt: createdAt,
                Url: this.Url(response.HtmlUrl),
                Truncated: lines.Count > MAX_BODY_LINES,
                Body: lines.Count > MAX_BODY_LINES ? [.. lines.Take(MAX_BODY_LINES)] : lines
            ),
            created
        );
    }

    private static IReadOnlyList<string> Lines(string body)
    {
        string trimmed = body.TrimEnd('\r', '\n');

        if (trimmed.Length == 0)
        {
            return [];
        }

        return [.. trimmed.Split('\n').Select(line => UpstreamText.ReplaceLineBreaking(line.TrimEnd('\r')))];
    }

    private string Id(long? value)
    {
        return value is > 0 ? value.Value.ToString(CultureInfo.InvariantCulture) : throw this.Incomplete();
    }

    // Only an absolute web link is accepted, so a relative or non-web value cannot be passed on as a link
    private Uri Url(string? value)
    {
        if (
            value is null
            || !Uri.TryCreate(uriString: value, uriKind: UriKind.Absolute, out Uri? uri)
            || !UriSchemes.IsHttp(uri)
        )
        {
            throw this.Incomplete();
        }

        return uri;
    }

    // A deleted account leaves no author, which is reported as an empty author rather than refused
    private string Author(IssueCommentUserResponse? user)
    {
        if (user is null)
        {
            return string.Empty;
        }

        string login = this.Header(user.Login);

        return this.Header(user.Type) switch
        {
            "User" => login,
            "Bot" => "app/" + StripBotSuffix(login),
            string type => type.ToLowerInvariant() + "/" + login,
        };
    }

    private static string StripBotSuffix(string login)
    {
        return login.EndsWith(value: BOT_SUFFIX, comparisonType: StringComparison.Ordinal)
            ? login[..^BOT_SUFFIX.Length]
            : login;
    }

    // Header values are printed one per line, so anything that could start a new line is flattened to a space
    private string Header(string? value)
    {
        if (value is null)
        {
            throw this.Incomplete();
        }

        return UpstreamText.ReplaceLineBreaking(value);
    }

    private Exception Incomplete()
    {
        return IssueCommentsFailure.Incomplete(this._reference);
    }
}
