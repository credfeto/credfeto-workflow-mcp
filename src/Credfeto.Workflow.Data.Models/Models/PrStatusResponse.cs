using System;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// Board is null when the repository has no Workflow board or the pull request has no state on it
[DebuggerDisplay("{Pr}: {State} {Board}")]
public sealed record PrStatusResponse(
    string Pr,
    string State,
    string Draft,
    string Title,
    Uri Url,
    string Base,
    string Head,
    string HeadSha,
    string AutoMerge,
    string ReviewDecision,
    string MergeState,
    string Mergeable,
    string Labels,
    string Assignees,
    string Author,
    int ChangedFiles,
    string? Board,
    string ReadyAt,
    string ReadySource,
    PrStatusChecks Checks,
    PrStatusActivity Activity
);
