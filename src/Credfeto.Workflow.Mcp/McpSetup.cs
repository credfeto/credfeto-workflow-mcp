using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Microsoft.AspNetCore.Builder;
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
        // before the MCP handler runs; the token itself is only validated by GitHub.
        // Wrapping each mapped endpoint's delegate (rather than an endpoint filter) keeps the SDK's
        // RequestDelegate endpoints off the reflection-based RequestDelegateFactory path, which AOT cannot use
        app.MapMcp(MCP_PATH).Finally(CallerTokenRequirement.Apply);
    }
}
