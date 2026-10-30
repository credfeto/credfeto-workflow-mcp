using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

[DebuggerDisplay("{Owner}/{Name}#{Number} comments={Comments} reviews={Reviews} threads={Threads}")]
internal sealed record PullRequestCommentsQueryVariables(
    [property: JsonPropertyName("o")] string Owner,
    [property: JsonPropertyName("r")] string Name,
    [property: JsonPropertyName("n")] int Number,
    [property: JsonPropertyName("comments")] bool Comments,
    [property: JsonPropertyName("reviews")] bool Reviews,
    [property: JsonPropertyName("threads")] bool Threads,
    [property: JsonPropertyName("commentsAfter")] string? CommentsAfter,
    [property: JsonPropertyName("reviewsAfter")] string? ReviewsAfter,
    [property: JsonPropertyName("threadsAfter")] string? ThreadsAfter
);
