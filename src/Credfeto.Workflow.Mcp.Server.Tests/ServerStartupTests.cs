using System;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Configuration;
using FunFair.Test.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Credfeto.Workflow.Mcp.Server.Tests;

public sealed class ServerStartupTests : TestBase
{
    [Theory]
    [InlineData("/ping")]
    [InlineData("/mcp/")]
    public static async Task BuildAppMapsEndpointAsync(string pattern)
    {
        await using WebApplication app = ServerStartup.BuildApp([$"--contentRoot={AppContext.BaseDirectory}"]);

        Assert.NotEmpty(EndpointLookup.FindEndpoints(app: app, pattern: pattern));
    }

    [Fact]
    public static async Task BuildAppHonoursCommandLineConfigurationOverrideAsync()
    {
        await using WebApplication app = ServerStartup.BuildApp([
            $"--contentRoot={AppContext.BaseDirectory}",
            "--GitHub:ApiBaseUrl=https://ghe.example.com/api/v3/",
        ]);

        GitHubOptions options = app.Services.GetRequiredService<IOptions<GitHubOptions>>().Value;

        Assert.Equal(expected: new Uri("https://ghe.example.com/api/v3/"), actual: options.ApiBaseUrl);
    }

    [Fact]
    public static async Task BuildAppEnablesDebugLoggingWhenConfiguredAsync()
    {
        await using WebApplication app = ServerStartup.BuildApp([
            $"--contentRoot={AppContext.BaseDirectory}",
            "--Logging:LogLevel:Default=Debug",
        ]);

        ILogger logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Credfeto.Workflow.Test");

        Assert.True(condition: logger.IsEnabled(LogLevel.Debug), userMessage: "Debug should be enabled");
    }
}
