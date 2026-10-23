using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Credfeto.Workflow.Mcp;

public static class McpSetup
{
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
        _ = app.MapMcp("/mcp");
    }
}
