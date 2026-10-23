using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

namespace Credfeto.Workflow.Mcp.Server;

public static class ServerRunner
{
    private const int EXIT_SUCCESS = 0;
    private const int EXIT_FAILURE = 1;

    public static async ValueTask<int> RunAsync(
        Func<WebApplication> appFactory,
        TextWriter errorWriter,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await RunUntilShutdownAsync(appFactory: appFactory, cancellationToken: cancellationToken);

            return EXIT_SUCCESS;
        }
        catch (Exception exception)
        {
            string message =
                $"Fatal error during startup:{Environment.NewLine}{exception.Message}{Environment.NewLine}{exception.StackTrace}";

            await errorWriter.WriteLineAsync(buffer: message.AsMemory(), cancellationToken: cancellationToken);

            return EXIT_FAILURE;
        }
    }

    private static async ValueTask RunUntilShutdownAsync(
        Func<WebApplication> appFactory,
        CancellationToken cancellationToken
    )
    {
        await using WebApplication app = appFactory();

        await app.StartAsync(cancellationToken);
        await app.WaitForShutdownAsync(cancellationToken);
    }
}
