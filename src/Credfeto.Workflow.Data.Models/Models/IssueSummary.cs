using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("#{Number}: {Title}, Priority={Priority}, Blocked={Blocked}, OnHold={OnHold}")]
public sealed record IssueSummary(
    int Number,
    string? Priority,
    bool Blocked,
    bool OnHold,
    IReadOnlyList<string> Assignees,
    string Title,
    IReadOnlyList<string> Labels
);
