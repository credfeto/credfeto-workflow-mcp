using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.GitHub.Workflows;

[DebuggerDisplay("Any ref: {AnyRef}, refs: {Refs.Count}")]
public sealed record DispatchAllowEntry(bool AnyRef, IReadOnlySet<string> Refs)
{
    // The default branch is always permitted for a listed workflow
    public bool Permits(string gitRef, string defaultBranch)
    {
        return this.AnyRef || StringComparer.Ordinal.Equals(x: gitRef, y: defaultBranch) || this.Refs.Contains(gitRef);
    }
}
