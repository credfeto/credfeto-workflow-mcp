using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("#{Number}: {Title}")]
public sealed record PullRequestListEntry(
    int Number,
    string Head,
    string Base,
    string Author,
    string State,
    IReadOnlyList<string> Flags,
    Uri Url,
    string Title
);
