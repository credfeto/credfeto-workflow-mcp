using System;
using System.IO;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Credfeto.Workflow.Mcp.Server.Helpers;

public static class KestrelConfig
{
    private const int HTTP_PORT = 8080;
    private const int HTTPS_PORT = 8081;
    private const string CERT_FILENAME = "server.pfx";

    public static IWebHostBuilder ConfigureKestrel(this IWebHostBuilder webHostBuilder)
    {
        return webHostBuilder.ConfigureKestrel(
            certPath: Path.Combine(path1: AppContext.BaseDirectory, path2: CERT_FILENAME)
        );
    }

    public static IWebHostBuilder ConfigureKestrel(this IWebHostBuilder webHostBuilder, string certPath)
    {
        return webHostBuilder.UseKestrel(options => ConfigureKestrelOptions(options: options, certPath: certPath));
    }

    private static void ConfigureKestrelOptions(KestrelServerOptions options, string certPath)
    {
        options.DisableStringReuse = false;
        options.AllowSynchronousIO = false;
        options.AddServerHeader = false;

        if (File.Exists(certPath))
        {
            options.ListenAnyIP(
                port: HTTPS_PORT,
                configure: listenOptions => ConfigureHttpsEndpoint(listenOptions: listenOptions, certFile: certPath)
            );
        }

        // Plain HTTP stays on loopback so only the in-container health check can reach it
        options.Listen(address: IPAddress.Loopback, port: HTTP_PORT, configure: ConfigureHttpEndpoint);
    }

    private static void ConfigureHttpEndpoint(ListenOptions listenOptions)
    {
        listenOptions.Protocols = HttpProtocols.Http1;
    }

    private static void ConfigureHttpsEndpoint(ListenOptions listenOptions, string certFile)
    {
        listenOptions.Protocols = HttpProtocols.Http1AndHttp2AndHttp3;
        _ = listenOptions.UseHttps(fileName: certFile, password: null);
    }
}
