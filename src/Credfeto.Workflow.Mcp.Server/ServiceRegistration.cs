using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.GitHub;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Credfeto.Workflow.Mcp.Server;

public static class ServiceRegistration
{
    public static WebApplicationBuilder AddWorkflowServices(this WebApplicationBuilder builder)
    {
        _ = builder
            .Services.AddGitHub()
            .AddMcpTools()
            .ConfigureHttpJsonOptions(static options =>
                options.SerializerOptions.TypeInfoResolverChain.Insert(index: 0, item: AppJsonContext.Default)
            );

        return builder;
    }
}
