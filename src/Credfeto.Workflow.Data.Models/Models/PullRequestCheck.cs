using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// Kind is check-run (status and conclusion set) or status (state set); the fields that do not apply are empty.
// Required is GitHub's own answer for this pull request, so it can differ from what the base branch's rules list
[DebuggerDisplay("{Kind} {Name}: {Status} {Conclusion} {State} {Required}")]
public sealed record PullRequestCheck(
    string Kind,
    string Name,
    string Status,
    string Conclusion,
    string State,
    bool Required
);
