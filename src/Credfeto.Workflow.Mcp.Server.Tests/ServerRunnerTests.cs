using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using FunFair.Test.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace Credfeto.Workflow.Mcp.Server.Tests;

public sealed class ServerRunnerTests : TestBase
{
    private const int EXIT_SUCCESS = 0;
    private const int EXIT_FAILURE = 1;

    [Fact]
    public async Task RunReturnsSuccessWhenServerStopsCleanlyAsync()
    {
        await using StringWriter errorWriter = new();

        int exitCode = await ServerRunner.RunAsync(
            appFactory: BuildSelfStoppingApp,
            errorWriter: errorWriter,
            cancellationToken: this.CancellationToken()
        );

        Assert.Equal(expected: EXIT_SUCCESS, actual: exitCode);
        Assert.Empty(errorWriter.ToString());
    }

    [Fact]
    public async Task RunReturnsFailureAndReportsErrorWhenStartupThrowsAsync()
    {
        await using StringWriter errorWriter = new();

        int exitCode = await ServerRunner.RunAsync(
            appFactory: static () =>
                throw new InvalidOperationException(
                    message: "Startup failed for test",
                    innerException: new FormatException("Root cause for test")
                ),
            errorWriter: errorWriter,
            cancellationToken: this.CancellationToken()
        );

        string output = errorWriter.ToString();

        Assert.Equal(expected: EXIT_FAILURE, actual: exitCode);
        Assert.StartsWith(
            expectedStartString: "Fatal error:",
            actualString: output,
            comparisonType: StringComparison.Ordinal
        );
        Assert.Contains(
            expectedSubstring: "Startup failed for test",
            actualString: output,
            comparisonType: StringComparison.Ordinal
        );
        Assert.Contains(
            expectedSubstring: "System.FormatException: Root cause for test",
            actualString: output,
            comparisonType: StringComparison.Ordinal
        );
    }

    private static WebApplication BuildSelfStoppingApp()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Configuration.Sources.Clear();

        // Port 0 binds an ephemeral loopback port so the test never collides with a fixed port
        _ = builder.WebHost.UseKestrel(static options => options.Listen(address: IPAddress.Loopback, port: 0));

        WebApplication app = builder.Build();
        _ = app.Lifetime.ApplicationStarted.Register(app.Lifetime.StopApplication);

        return app;
    }
}
