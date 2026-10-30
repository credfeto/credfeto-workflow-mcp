using System;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Id}: {Author} {State}")]
public sealed record PullRequestReview(
    string Id,
    string Author,
    string Association,
    string State,
    string SubmittedAt,
    Uri Url,
    string Body
);
