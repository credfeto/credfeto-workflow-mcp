using System.Diagnostics;

namespace Credfeto.Workflow.GitHub.Labels;

[DebuggerDisplay("{Repository}#{Number}")]
internal sealed record LabelTarget(string Repository, int Number, bool IsPullRequest);
