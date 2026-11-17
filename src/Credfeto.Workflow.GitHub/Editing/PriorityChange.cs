using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Credfeto.Workflow.GitHub.Editing;

// Labels is the full set to write, or null when the priority needs no write; OldPriority names the priority labels the
// item had, or is null when it had none
[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay("{OldPriority} -> {NewPriority}")]
internal readonly record struct PriorityChange(
    IReadOnlyList<string>? Labels,
    string? OldPriority,
    string? NewPriority,
    bool LabelIsOnItem
)
{
    public static PriorityChange None { get; } =
        new(Labels: null, OldPriority: null, NewPriority: null, LabelIsOnItem: true);
}
