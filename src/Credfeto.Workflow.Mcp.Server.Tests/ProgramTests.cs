using System.Threading.Tasks;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Server.Tests;

public sealed class ProgramTests : LoggingFolderCleanupTestBase
{
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
}
