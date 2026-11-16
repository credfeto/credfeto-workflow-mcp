using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// Size and Content are only given for a File, and Entries only for a Directory; Path and Ref are left out of a NotFound
// result so it never names what was asked for
[DebuggerDisplay("{Kind}: {Message}")]
public sealed record RepoFileResponse(
    RepoFileKind Kind,
    string Message,
    string Repository,
    string? Path,
    string? Ref,
    long? Size,
    string? Content,
    IReadOnlyList<RepoFileEntry>? Entries
);
