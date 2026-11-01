using System.Diagnostics;
using System.Globalization;

namespace Credfeto.Workflow.GitHub.Leaks;

// The repository is deliberately left out of the debugger display and the text form so it is never shown alongside a
// refusal
[DebuggerDisplay("Line {LineNumber}")]
public sealed record RepositoryReference(int LineNumber, string Owner, string Name)
{
    public string Repository => this.Owner + "/" + this.Name;

    public override string ToString()
    {
        return "Line " + this.LineNumber.ToString(CultureInfo.InvariantCulture);
    }
}
