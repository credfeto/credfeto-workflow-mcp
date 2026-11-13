using System;
using System.Collections.Generic;
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
        if (HealthCheckClient.IsHealthCheck(args: args, out string? checkUrl))
        {
            return await HealthCheckRunner.ExecuteAsync(checkUrl: checkUrl, cancellationToken: CancellationToken.None);
        }

        if (SelfTestRunner.IsSelfTest(args))
        {
            IReadOnlyList<string> serverArgs = SelfTestRunner.ServerArguments(args);

            return await SelfTestRunner.RunAsync(
                appFactory: () => ServerStartup.BuildApp(serverArgs),
                errorWriter: Console.Error,
                cancellationToken: CancellationToken.None
            );
        }

        return await ServerRunner.RunAsync(
            appFactory: () => ServerStartup.BuildApp(args),
            errorWriter: Console.Error,
            cancellationToken: CancellationToken.None
        );
    }
}
