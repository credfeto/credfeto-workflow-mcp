using System;
using System.Diagnostics;

namespace Credfeto.Workflow.GitHub.PullRequests;

// CommentId and Url are set only when the comment was posted; otherwise Rejected tells a comment GitHub refused from
// one whose request was sent but whose answer could not be trusted, and Error says why with no upstream detail
[DebuggerDisplay("CommentId={CommentId}, Rejected={Rejected}, Error={Error}")]
internal sealed record ReviewCommentPostAnswer(long? CommentId, Uri? Url, bool Rejected, string Error);
