using System.Threading;
using System.Threading.Tasks;
using Credfeto.Docker.HealthCheck.Http.Client;
using Serilog.Extensions.Logging;

namespace Credfeto.Workflow.Mcp.Server.Helpers;

public static class HealthCheckRunner
{
    public static async ValueTask<int> ExecuteAsync(string checkUrl, CancellationToken cancellationToken)
    {
        using SerilogLoggerFactory loggerFactory = new(
            logger: LoggingConfig.CreateConfiguration().CreateLogger(),
            dispose: true
        );

        return await HealthCheckClient.ExecuteAsync(
            targetUrl: checkUrl,
            logger: loggerFactory.CreateLogger("HealthCheck"),
            cancellationToken: cancellationToken
        );
    }
}
