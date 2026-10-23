using Microsoft.AspNetCore.Builder;

namespace Credfeto.Workflow.Mcp.Server.Tests;

internal static class TestAppFactory
{
    public static WebApplication BuildApp()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();

        builder.Configuration.Sources.Clear();

        _ = builder.AddWorkflowServices();

        return builder.Build();
    }
}
