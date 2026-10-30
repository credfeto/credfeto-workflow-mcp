using System.Diagnostics;

namespace Credfeto.Workflow.GitHub.Interfaces.Models;

// A null AppId means the check may be reported by any source
[DebuggerDisplay("Name={Name}, AppId={AppId}")]
public sealed record RequiredCheck(string Name, long? AppId);
