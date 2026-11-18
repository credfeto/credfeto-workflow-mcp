using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using ModelContextProtocol.Server;

namespace Credfeto.Workflow.Mcp.Modes;

// Built from the registered tools' own InModes attributes, so there is no second list of tool names to keep in step
public sealed class ToolModeCatalog
{
    private readonly FrozenDictionary<string, ToolModes> _modes;

    public ToolModeCatalog(IEnumerable<McpServerTool> tools)
    {
        this._modes = tools.ToFrozenDictionary(
            keySelector: static tool => tool.ProtocolTool.Name,
            elementSelector: ModesOf,
            comparer: StringComparer.Ordinal
        );
    }

    public static ToolModes ModesOf(McpServerTool tool)
    {
        return tool.Metadata.OfType<InModesAttribute>().FirstOrDefault()?.Modes ?? ToolModes.None;
    }

    // In every mode but All, a name that is not a tool is refused like a tool outside the mode, so the answer never
    // depends on whether the name exists
    public bool IsAvailable(string name, ToolModes mode)
    {
        return this._modes.TryGetValue(key: name, out ToolModes modes)
            ? (modes & mode) != ToolModes.None
            : mode == ToolModes.All;
    }

    public IReadOnlyList<string> ToolsIn(ToolModes mode)
    {
        return
        [
            .. this
                ._modes.Where(entry => (entry.Value & mode) != ToolModes.None)
                .Select(static entry => entry.Key)
                .Order(StringComparer.Ordinal),
        ];
    }
}
