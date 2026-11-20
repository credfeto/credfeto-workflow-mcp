using System.Diagnostics;
using Credfeto.Workflow.GitHub.Interfaces.Models;

namespace Credfeto.Workflow.GitHub.Checks;

// A check to judge, and whether the branch rules require it or it is only judged because it reported
[DebuggerDisplay("{Check.Name}: {Required}")]
internal readonly record struct Requirement(RequiredCheck Check, bool Required);
