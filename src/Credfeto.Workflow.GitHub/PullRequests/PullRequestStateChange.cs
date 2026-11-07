using System.Diagnostics;

namespace Credfeto.Workflow.GitHub.PullRequests;

// Close and reopen differ only in the state asked for and the words reported, so one flow serves both
[DebuggerDisplay("{Verb}: {Target}")]
internal sealed record PullRequestStateChange(string Target, string Verb, string Outcome, string AlreadyOutcome)
{
    public static PullRequestStateChange Close { get; } =
        new(Target: "closed", Verb: "close", Outcome: "closed", AlreadyOutcome: "already-closed");

    public static PullRequestStateChange Reopen { get; } =
        new(Target: "open", Verb: "reopen", Outcome: "open", AlreadyOutcome: "already-open");
}
