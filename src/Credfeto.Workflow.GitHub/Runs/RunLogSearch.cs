using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Credfeto.Workflow.GitHub.Runs;

[DebuggerDisplay("{Pattern} context: {Context} max: {MaxMatches} from: {FromLine}")]
internal sealed record RunLogSearch(Regex Pattern, int Context, int MaxMatches, int FromLine);
