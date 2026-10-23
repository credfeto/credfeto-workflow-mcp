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
        string[] commandLine = [.. args];
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(commandLine);

        builder.Configuration.Sources.Clear();
        _ = builder
            .Configuration.SetBasePath(builder.Environment.ContentRootPath)
            .AddJsonFile(path: "appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile(path: "appsettings-local.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .AddCommandLine(commandLine);

        ConfigureLogging(builder);

        _ = builder.WebHost.ConfigureKestrel();

        _ = builder.AddWorkflowServices();

        return builder.Build().MapWorkflowEndpoints();
    }

    public static WebApplication MapWorkflowEndpoints(this WebApplication app)
    {
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
