using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Credfeto.Workflow.Mcp.Server.Helpers;
using FunFair.Test.Common;
using Microsoft.AspNetCore.Builder;
using Xunit;

namespace Credfeto.Workflow.Mcp.Server.Tests;

public sealed class SelfTestRunnerTests : TestBase
{
    private const int EXIT_SUCCESS = 0;
    private const int EXIT_FAILURE = 1;

    [Fact]
    public async Task RunReturnsSuccessWithoutStartingTheAppAsync()
    {
        await using StringWriter errorWriter = new();
        bool started = false;

        int exitCode = await SelfTestRunner.RunAsync(
            appFactory: () =>
            {
                WebApplication app = ServerStartup.BuildApp([$"--contentRoot={AppContext.BaseDirectory}"]);
                _ = app.Lifetime.ApplicationStarted.Register(() => started = true);

                return app;
            },
            errorWriter: errorWriter,
            cancellationToken: this.CancellationToken()
        );

        Assert.Equal(expected: EXIT_SUCCESS, actual: exitCode);
        Assert.Empty(errorWriter.ToString());
        Assert.False(condition: started, userMessage: "The self-test must not start the app");
    }

    [Fact]
    public async Task RunReturnsFailureWithASingleLineReasonAsync()
    {
        await using StringWriter errorWriter = new();

        int exitCode = await SelfTestRunner.RunAsync(
            appFactory: static () =>
                throw new InvalidOperationException(
                    message: "Startup failed" + Environment.NewLine + "for test",
                    innerException: new FormatException("Root cause for test")
                ),
            errorWriter: errorWriter,
            cancellationToken: this.CancellationToken()
        );

        string output = errorWriter.ToString().TrimEnd();

        Assert.Equal(expected: EXIT_FAILURE, actual: exitCode);
        Assert.StartsWith(
            expectedStartString: "Self-test failed: System.InvalidOperationException: Startup failed for test",
            actualString: output,
            comparisonType: StringComparison.Ordinal
        );
        Assert.Contains(
            expectedSubstring: "System.FormatException: Root cause for test",
            actualString: output,
            comparisonType: StringComparison.Ordinal
        );
        Assert.DoesNotContain(expectedSubstring: "\n", actualString: output, comparisonType: StringComparison.Ordinal);
        Assert.DoesNotContain(expectedSubstring: "\r", actualString: output, comparisonType: StringComparison.Ordinal);
    }

    [Fact]
    public static void IsSelfTestDetectsTheSwitchOnItsOwn()
    {
        Assert.True(condition: SelfTestRunner.IsSelfTest(["--self-test"]), userMessage: "Switch on its own");
    }

    [Fact]
    public static void IsSelfTestDetectsTheSwitchAfterOtherArguments()
    {
        Assert.True(
            condition: SelfTestRunner.IsSelfTest(["--contentRoot=/app", "--self-test"]),
            userMessage: "Switch after another argument"
        );
    }

    [Fact]
    public static void IsSelfTestIgnoresNoArguments()
    {
        Assert.False(condition: SelfTestRunner.IsSelfTest([]), userMessage: "No arguments");
    }

    [Fact]
    public static void IsSelfTestIgnoresTheSwitchWithAValue()
    {
        Assert.False(condition: SelfTestRunner.IsSelfTest(["--self-test=true"]), userMessage: "Switch with a value");
    }

    [Fact]
    public static void IsSelfTestIgnoresTheHealthCheck()
    {
        Assert.False(
            condition: SelfTestRunner.IsSelfTest(["--health-check", "http://127.0.0.1:8080/ping"]),
            userMessage: "Health check"
        );
    }

    [Fact]
    public static void ServerArgumentsRemovesOnlyTheSwitch()
    {
        IReadOnlyList<string> serverArgs = SelfTestRunner.ServerArguments([
            "--self-test",
            "--contentRoot=/app",
            "--GitHub:ApiBaseUrl=https://ghe.example.com/api/v3/",
        ]);

        Assert.Equal(
            expected: ["--contentRoot=/app", "--GitHub:ApiBaseUrl=https://ghe.example.com/api/v3/"],
            actual: serverArgs
        );
    }
}
