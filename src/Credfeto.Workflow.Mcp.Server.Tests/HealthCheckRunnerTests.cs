using System.Threading.Tasks;
using Credfeto.Workflow.Mcp.Server.Helpers;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Server.Tests;

public sealed class HealthCheckRunnerTests : TestBase
{
    private const int HEALTHCHECK_FAIL = 1;

    [Fact]
    public async Task InvalidUrlFailsHealthCheckAsync()
    {
        int exitCode = await HealthCheckRunner.ExecuteAsync(
            checkUrl: "not-a-url",
            cancellationToken: this.CancellationToken()
        );

        Assert.Equal(expected: HEALTHCHECK_FAIL, actual: exitCode);
    }
}
