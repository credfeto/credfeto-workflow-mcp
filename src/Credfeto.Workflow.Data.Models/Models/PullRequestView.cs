using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// Only the requested fields are populated; the rest stay null so they are left out of the serialised result
[DebuggerDisplay("{Pr}: {State}")]
public sealed record PullRequestView(
    string Pr,
    string? State,
    string? Draft,
    string? Title,
    Uri? Url,
    string? Base,
    string? Head,
    string? HeadSha,
    string? AutoMerge,
    string? ReviewDecision,
    string? MergeState,
    string? Mergeable,
    string? CreatedAt,
    string? MergedAt,
    string? MergeCommit,
    string? Labels,
    string? Assignees,
    string? Author,
    string? CommitAuthors,
    IReadOnlyList<string>? Files,
    string? Body
);
