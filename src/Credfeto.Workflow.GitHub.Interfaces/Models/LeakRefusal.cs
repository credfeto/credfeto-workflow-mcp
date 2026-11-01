using System.Diagnostics;

namespace Credfeto.Workflow.GitHub.Interfaces.Models;

// Holds no text: the matched text is never surfaced so a refusal cannot echo the leak back. A null LineNumber means
// the rule applies to the text as a whole rather than to one line.
[DebuggerDisplay("Subject={Subject}, Rule={Rule}, Line={LineNumber}")]
public sealed record LeakRefusal(LeakSubject Subject, LeakRule Rule, int? LineNumber);
