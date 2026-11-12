using System.Diagnostics;

namespace Credfeto.Workflow.GitHub.Runs;

// Number is the line's number in its step's log, set only when the log was searched; Match is false for a line kept
// only as context around a match
[DebuggerDisplay("{Number}: {Line.Text}")]
internal readonly record struct RunLogEntry(RunLogLine Line, int? Number, bool Match);
