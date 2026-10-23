using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("Value={Value}")]
public readonly record struct PingResponse(string Value);
