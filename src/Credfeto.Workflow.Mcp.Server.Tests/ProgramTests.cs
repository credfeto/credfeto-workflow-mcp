using System;
using System.Threading.Tasks;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Server.Tests;

public sealed class ProgramTests : LoggingFolderCleanupTestBase
{
    private const int EXIT_SUCCESS = 0;
    private const int EXIT_FAILURE = 1;

    public ProgramTests(ITestOutputHelper output)
        : base(output) { }

    [Fact]
    public async Task HealthCheckModeFailsForInvalidUrlAsync()
    {
        int exitCode = await Program.Main(["--health-check", "not-a-url"]);

        Assert.Equal(expected: EXIT_FAILURE, actual: exitCode);
    }

    [Fact]
    public async Task ServerModeFailsWhenAppSettingsAreMissingAsync()
    {
        int exitCode = await Program.Main([$"--contentRoot={this.TempFolder}"]);

        Assert.Equal(expected: EXIT_FAILURE, actual: exitCode);
    }

    [Fact]
    public async Task SelfTestModeSucceedsWithValidConfigurationAsync()
    {
        int exitCode = await Program.Main(["--self-test", $"--contentRoot={AppContext.BaseDirectory}"]);

        Assert.Equal(expected: EXIT_SUCCESS, actual: exitCode);
    }

    [Fact]
    public async Task SelfTestModeFailsWhenAppSettingsAreMissingAsync()
    {
        int exitCode = await Program.Main(["--self-test", $"--contentRoot={this.TempFolder}"]);

        Assert.Equal(expected: EXIT_FAILURE, actual: exitCode);
    }
}
