using System.Collections.Generic;
using Credfeto.Workflow.Mcp.Server.Endpoints;
using Credfeto.Workflow.Mcp.Server.Helpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Credfeto.Workflow.Mcp.Server;

public static class ServerStartup
{
    public static WebApplication BuildApp(IReadOnlyList<string> args)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder([.. args]);

        builder.Configuration.Sources.Clear();
        _ = builder
            .Configuration.SetBasePath(builder.Environment.ContentRootPath)
            .AddJsonFile(path: "appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile(path: "appsettings-local.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();

        ConfigureLogging(builder);

        _ = builder.WebHost.ConfigureKestrel();

        _ = builder.AddWorkflowServices();

        WebApplication app = builder.Build();

        app.MapPingEndpoints();
        app.MapMcpEndpoint();

        return app;
    }

    private static void ConfigureLogging(WebApplicationBuilder builder)
    {
        _ = builder
            .Logging.ClearProviders()
            .AddSerilog(logger: LoggingConfig.CreateConfiguration().CreateLogger(), dispose: true);
    }
}
