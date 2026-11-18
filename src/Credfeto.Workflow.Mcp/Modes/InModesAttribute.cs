using System;

namespace Credfeto.Workflow.Mcp.Modes;

// Every MCP tool must carry this, so a new tool has to say which modes it is available in; a tool without it is in
// no mode at all and a test fails
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InModesAttribute : Attribute
{
    public InModesAttribute(ToolModes modes)
    {
        this.Modes = modes;
    }

    public ToolModes Modes { get; }
}
