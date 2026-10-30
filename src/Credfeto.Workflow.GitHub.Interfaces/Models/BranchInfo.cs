using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.GitHub.Interfaces.Models;

[DebuggerDisplay("Sha={Sha}, Checks={Checks.Count}")]
public sealed record BranchInfo(string Sha, IReadOnlyList<RequiredCheck> Checks);
