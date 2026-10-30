using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Repo} {Branch}: {Checks.Count} checks")]
public sealed record BranchRequiredChecksResponse(string Repo, string Branch, IReadOnlyList<string> Checks);
