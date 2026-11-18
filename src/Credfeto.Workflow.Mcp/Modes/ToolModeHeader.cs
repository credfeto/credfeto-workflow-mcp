using System;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Credfeto.Workflow.Mcp.Modes;

public static class ToolModeHeader
{
    public const string Name = "X-Mode";

    public const string ModeNames = "All, Issues";

    private static readonly (string Name, ToolModes Mode)[] Modes =
    [
        ("All", ToolModes.All),
        ("Issues", ToolModes.Issues),
    ];

    // No header keeps every tool, as before modes existed; anything that is not exactly one known mode name is refused
    // rather than widened to every tool, so a typo can never give a caller more than it asked for
    public static bool TryRead(HttpRequest request, out ToolModes mode)
    {
        StringValues values = request.Headers[Name];

        if (values.Count == 0)
        {
            mode = ToolModes.All;

            return true;
        }

        if (values.Count == 1)
        {
            return TryParse(value: values[0], mode: out mode);
        }

        mode = ToolModes.None;

        return false;
    }

    public static string NameOf(ToolModes mode)
    {
        foreach ((string name, ToolModes known) in Modes)
        {
            if (known == mode)
            {
                return name;
            }
        }

        throw new ArgumentOutOfRangeException(paramName: nameof(mode), message: "The mode has no name");
    }

    private static bool TryParse(string? value, out ToolModes mode)
    {
        string trimmed = value?.Trim() ?? string.Empty;

        foreach ((string name, ToolModes known) in Modes)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(x: name, y: trimmed))
            {
                mode = known;

                return true;
            }
        }

        mode = ToolModes.None;

        return false;
    }
}
