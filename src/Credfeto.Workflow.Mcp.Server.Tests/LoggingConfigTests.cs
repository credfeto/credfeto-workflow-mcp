using Credfeto.Workflow.Mcp.Server.Helpers;
using FunFair.Test.Common;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace Credfeto.Workflow.Mcp.Server.Tests;

public sealed class LoggingConfigTests : TestBase
{
    [Fact]
    public void CreateLoggerEnablesInformation()
    {
        using Logger logger = LoggingConfig.CreateConfiguration().CreateLogger();

        Assert.True(
            condition: logger.IsEnabled(LogEventLevel.Information),
            userMessage: "Information should be enabled"
        );
    }
}
