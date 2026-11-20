using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// ClosingIssues is only read when asked for, so it stays null, and out of the serialised result, otherwise
[DebuggerDisplay("#{Number}: {Title}")]
public sealed record PullRequestListEntry(
    int Number,
    string Head,
    string Base,
    string Author,
    string State,
    IReadOnlyList<string> Flags,
    Uri Url,
    string Title,
    IReadOnlyList<string> Labels,
    IReadOnlyList<ClosingIssueReference>? ClosingIssues
);
