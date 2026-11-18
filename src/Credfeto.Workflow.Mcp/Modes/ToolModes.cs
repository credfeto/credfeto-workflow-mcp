using System;

namespace Credfeto.Workflow.Mcp.Modes;

// A request asks for exactly one mode; a tool lists every mode it is available in
[Flags]
public enum ToolModes
{
    None = 0,
    All = 1,
    Issues = 2,
}
