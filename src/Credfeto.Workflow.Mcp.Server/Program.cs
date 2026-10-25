using System;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Docker.HealthCheck.Http.Client;
using Credfeto.Workflow.Mcp.Server.Helpers;

namespace Credfeto.Workflow.Mcp.Server;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // The process entry point has no caller-supplied token; host shutdown is driven by the
        // application lifetime (SIGTERM/Ctrl+C) instead
        return HealthCheckClient.IsHealthCheck(args: args, out string? checkUrl)
            ? await HealthCheckRunner.ExecuteAsync(checkUrl: checkUrl, cancellationToken: CancellationToken.None)
            : await ServerRunner.RunAsync(
                appFactory: () => ServerStartup.BuildApp(args),
                errorWriter: Console.Error,
                cancellationToken: CancellationToken.None
            );
    }
}
