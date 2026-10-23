using System;
using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Credfeto.Workflow.Mcp;

public static class McpSetup
{
    private const string MCP_PATH = "/mcp";

    public static IServiceCollection AddMcpTools(this IServiceCollection services)
    {
        JsonSerializerOptions serializerOptions = new(options: AppJsonContext.Default.Options);

        _ = services
            .AddMcpServer()
            .WithHttpTransport(static options => options.Stateless = true)
            .WithTools<WorkflowMcpTools>(serializerOptions: serializerOptions);

        return services;
    }

    public static void MapMcpEndpoint(this WebApplication app)
    {
        // Every MCP call acts on GitHub with the caller's own token, so reject requests without one
        // before the MCP handler runs; the token itself is only validated by GitHub
        _ = app.UseWhen(
            predicate: IsMcpRequest,
            configuration: static branch => branch.Use(CallerTokenRequirement.InvokeAsync)
        );
        _ = app.MapMcp(MCP_PATH);
    }

    private static bool IsMcpRequest(HttpContext context)
    {
        return context.Request.Path.StartsWithSegments(
            other: MCP_PATH,
            comparisonType: StringComparison.OrdinalIgnoreCase
        );
    }
}
