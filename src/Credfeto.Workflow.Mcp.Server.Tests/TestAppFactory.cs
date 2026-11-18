using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

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

    // The whole server on an ephemeral loopback port, for tests that talk to it over HTTP
    public static WebApplication BuildLoopbackApp()
    {
        return BuildApp(ConfigureForLoopback).MapWorkflowEndpoints();
    }

    private static void ConfigureForLoopback(WebApplicationBuilder builder)
    {
        _ = builder.Configuration.AddInMemoryCollection([
            new KeyValuePair<string, string?>(key: "GitHub:ApiBaseUrl", value: "https://api.github.com/"),
        ]);

        // Port 0 binds an ephemeral loopback port so the test never collides with a fixed port
        _ = builder.WebHost.UseKestrel(static options => options.Listen(address: IPAddress.Loopback, port: 0));
    }
}
