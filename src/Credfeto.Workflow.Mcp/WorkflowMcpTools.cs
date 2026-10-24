using Credfeto.Workflow.Data.Models.Models;
using ModelContextProtocol.Server;

namespace Credfeto.Workflow.Mcp;

[McpServerToolType]
public sealed class WorkflowMcpTools
{
    private const string PONG = "pong";

    [McpServerTool(Name = "ping", Title = "Ping")]
    public static PingResponse Ping()
    {
        return new(PONG);
    }
}
