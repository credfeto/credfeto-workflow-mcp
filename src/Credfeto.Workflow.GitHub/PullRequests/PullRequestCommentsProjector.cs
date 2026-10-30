using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Http;

namespace Credfeto.Workflow.GitHub.PullRequests;

// Turns upstream nodes into the tool's output, refusing any node with a missing or malformed required field
[DebuggerDisplay("{_reference}")]
internal sealed partial class PullRequestCommentsProjector
{
    private const int MAX_THREAD_COMMENTS = 100;

    private const int REGEX_TIMEOUT_MILLISECONDS = 1000;

    private const string COMMENTED_STATE = "COMMENTED";

    private const char LINE_SEPARATOR = (char)0x2028;

    private const char PARAGRAPH_SEPARATOR = (char)0x2029;

    private static readonly SearchValues<char> LineBreakingCharacters = SearchValues.Create([
        .. Enumerable.Range(start: 0x00, count: 0x20).Select(static c => (char)c),
        .. Enumerable.Range(start: 0x7F, count: 0x21).Select(static c => (char)c),
        LINE_SEPARATOR,
        PARAGRAPH_SEPARATOR,
    ]);

    private readonly string _reference;

    public PullRequestCommentsProjector(string reference)
    {
        this._reference = reference;
    }

    public PullRequestComment Comment(IssueCommentNode node)
    {
        return new(
            Id: this.Id(node.FullDatabaseId),
            Author: this.Author(node.Author),
            Association: this.Header(node.AuthorAssociation),
            CreatedAt: this.Header(node.CreatedAt),
            Minimized: node.IsMinimized ?? throw this.Incomplete(),
            Url: this.Url(node.Url),
            Body: this.Body(node.Body)
        );
    }

    public PullRequestReview Review(ReviewNode node)
    {
        return new(
            Id: this.Id(node.FullDatabaseId),
            Author: this.Author(node.Author),
            Association: this.Header(node.AuthorAssociation),
            State: this.Header(node.State),
            SubmittedAt: node.SubmittedAt is null ? string.Empty : this.Header(node.SubmittedAt),
            Url: this.Url(node.Url),
            Body: this.Body(node.Body)
        );
    }

    public PullRequestThread Thread(ReviewThreadNode node)
    {
        IReadOnlyList<PullRequestInlineComment> comments = this.ThreadComments(node.Comments);

        return new(
            Id: comments[0].Id,
            Resolved: node.IsResolved ?? throw this.Incomplete(),
            Outdated: node.IsOutdated ?? throw this.Incomplete(),
            Path: this.Header(node.Path),
            Line: LineNumber(node.Line),
            OriginalLine: LineNumber(node.OriginalLine),
            CommentCount: comments.Count,
            Comments: comments
        );
    }

    // The wrapper review GitHub creates around inline comments carries no text of its own, so it is only counted
    public static bool IsEmptyWrapper(PullRequestReview review)
    {
        return StringComparer.Ordinal.Equals(x: review.State, y: COMMENTED_STATE) && IsBlank(review.Body);
    }

    private IReadOnlyList<PullRequestInlineComment> ThreadComments(GraphQlConnection<ReviewCommentNode>? connection)
    {
        // The query reads one page of each thread's comments, so a longer thread cannot be returned whole
        if (
            connection?.TotalCount is not (>= 1 and <= MAX_THREAD_COMMENTS)
            || connection.PageInfo?.HasNextPage is not false
            || connection.Nodes is null
            || connection.Nodes.Count != connection.TotalCount
        )
        {
            throw this.Incomplete();
        }

        return [.. connection.Nodes.Select(node => this.InlineComment(node ?? throw this.Incomplete()))];
    }

    private PullRequestInlineComment InlineComment(ReviewCommentNode node)
    {
        return new(
            Id: this.Id(node.FullDatabaseId),
            InReplyTo: this.OptionalId(node.ReplyTo),
            ReviewId: this.OptionalId(node.PullRequestReview),
            Author: this.Author(node.Author),
            Association: this.Header(node.AuthorAssociation),
            CreatedAt: this.Header(node.CreatedAt),
            Minimized: node.IsMinimized ?? throw this.Incomplete(),
            Url: this.Url(node.Url),
            Body: this.Body(node.Body)
        );
    }

    private string OptionalId(DatabaseIdNode? node)
    {
        return node is null ? string.Empty : this.Id(node.FullDatabaseId);
    }

    private string Id(string? value)
    {
        if (value is null || !DatabaseId().IsMatch(value))
        {
            throw this.Incomplete();
        }

        return value;
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
    private string Author(AuthorNode? author)
    {
        if (author is null)
        {
            return string.Empty;
        }

        if (author.TypeName is null || author.Login is null)
        {
            throw this.Incomplete();
        }

        string login = this.Header(author.Login);

        return author.TypeName switch
        {
            "User" => login,
            "Bot" => "app/" + login,
            _ => this.Header(author.TypeName).ToLowerInvariant() + "/" + login,
        };
    }

    // Header values are printed one per line, so anything that could start a new line is flattened to a space
    private string Header(string? value)
    {
        if (value is null)
        {
            throw this.Incomplete();
        }

        if (!value.AsSpan().ContainsAny(LineBreakingCharacters))
        {
            return value;
        }

        return string.Create(
            length: value.Length,
            state: value,
            action: static (span, source) =>
            {
                for (int index = 0; index < source.Length; ++index)
                {
                    char character = source[index];
                    span[index] = IsLineBreaking(character) ? ' ' : character;
                }
            }
        );
    }

    private string Body(string? value)
    {
        return value?.TrimEnd('\r', '\n') ?? throw this.Incomplete();
    }

    // The line and paragraph separators end a line without being control characters
    private static bool IsLineBreaking(char character)
    {
        return char.IsControl(character) || character is LINE_SEPARATOR or PARAGRAPH_SEPARATOR;
    }

    private static bool IsBlank(string body)
    {
        return body.AsSpan().IndexOfAnyExcept(" \t\r\n".AsSpan()) < 0;
    }

    private static string LineNumber(int? line)
    {
        return line?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private Exception Incomplete()
    {
        return PullRequestCommentsFailure.Incomplete(this._reference);
    }

    // \z rather than $ because in .NET $ also matches before a trailing newline, which would let one through
    [GeneratedRegex(
        pattern: @"^[0-9]+\z",
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: REGEX_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex DatabaseId();
}
