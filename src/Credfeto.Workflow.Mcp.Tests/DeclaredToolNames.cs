using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ModelContextProtocol.Server;

namespace Credfeto.Workflow.Mcp.Tests;

// The tools are registered with WithTools<WorkflowMcpTools>, so the expected names are read from the same
// [McpServerTool] attributes the SDK reads; a new tool needs no edit to a test list or count
internal static class DeclaredToolNames
{
    public static IReadOnlyList<string> All { get; } =
    [
        .. typeof(WorkflowMcpTools)
            .GetMethods(
                BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.Static
                    | BindingFlags.Instance
                    | BindingFlags.DeclaredOnly
            )
            .Select(static method => (method, attribute: method.GetCustomAttribute<McpServerToolAttribute>()))
            .Where(static tool => tool.attribute is not null)
            .Select(static tool =>
                tool.attribute?.Name
                ?? throw new InvalidOperationException(tool.method.Name + " must give its tool a Name")
            )
            .Order(StringComparer.Ordinal),
    ];
}
