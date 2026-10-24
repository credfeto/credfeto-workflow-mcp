using System;
using Microsoft.AspNetCore.Builder;

namespace Credfeto.Workflow.Mcp.Server.Tests;

internal static class TestAppFactory
{
    public static WebApplication BuildApp()
    {
        return BuildApp(static _ => { });
    }

    public static WebApplication BuildApp(Action<WebApplicationBuilder> configure)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();

        builder.Configuration.Sources.Clear();

        configure(builder);

        _ = builder.AddWorkflowServices();

        return builder.Build();
    }
}
