using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Credfeto.Workflow.Mcp.Modes;

public static class ToolModeFilters
{
    public static McpRequestHandler<ListToolsRequestParams, ListToolsResult> ListTools(
        McpRequestHandler<ListToolsRequestParams, ListToolsResult> next
    )
    {
        return (context, cancellationToken) =>
            ListToolsAsync(next: next, context: context, cancellationToken: cancellationToken);
    }

    public static McpRequestHandler<CallToolRequestParams, CallToolResult> CallTool(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next
    )
    {
        return (context, cancellationToken) =>
        {
            (ToolModes mode, ToolModeCatalog catalog) = Resolve(context.Services);

            // Refused before the tool runs, so a tool outside the mode can never act, whether or not it was listed
            return catalog.IsAvailable(name: context.Params?.Name ?? string.Empty, mode: mode)
                ? next(context, cancellationToken)
                : throw new McpProtocolException(
                    message: NotAvailableMessage(mode: mode, catalog: catalog),
                    errorCode: McpErrorCode.InvalidParams
                );
        };
    }

    public static string NotAvailableMessage(ToolModes mode, ToolModeCatalog catalog)
    {
        string name = ToolModeHeader.NameOf(mode);

        return $"The tool is not available in {ToolModeHeader.Name} {name}; the {name} tools are: {string.Join(separator: ", ", values: catalog.ToolsIn(mode))}";
    }

    private static async ValueTask<ListToolsResult> ListToolsAsync(
        McpRequestHandler<ListToolsRequestParams, ListToolsResult> next,
        RequestContext<ListToolsRequestParams> context,
        CancellationToken cancellationToken
    )
    {
        ListToolsResult result = await next(context, cancellationToken);

        (ToolModes mode, ToolModeCatalog catalog) = Resolve(context.Services);

        if (mode == ToolModes.All)
        {
            return result;
        }

        result.Tools = [.. result.Tools.Where(tool => catalog.IsAvailable(name: tool.Name, mode: mode))];

        return result;
    }

    private static (ToolModes Mode, ToolModeCatalog Catalog) Resolve(IServiceProvider? services)
    {
        // Fail closed: without the request's services there is no way to know the caller's mode
        IServiceProvider provider = services
            ?? throw new InvalidOperationException("The MCP request has no services, so its tool mode is unknown.");

        return (
            provider.GetRequiredService<IRequestToolMode>().Current,
            provider.GetRequiredService<ToolModeCatalog>()
        );
    }
}
