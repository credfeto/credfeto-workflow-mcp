using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Credfeto.Workflow.GitHub.Editing;

[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay("{Repository}#{Number}")]
internal readonly record struct EditRequest(string Repository, int Number, string? Title, string? Body, bool AssignSelf)
{
    public string NumberText => this.Number.ToString(CultureInfo.InvariantCulture);

    public string Reference => $"{this.Repository}#{this.NumberText}";
}
