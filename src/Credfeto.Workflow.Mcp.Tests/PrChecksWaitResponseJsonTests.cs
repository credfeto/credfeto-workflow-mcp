using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class PrChecksWaitResponseJsonTests : TestBase
{
    private const string SHA = "0123456789abcdef0123456789abcdef01234567";

    private const string JSON =
        "{\"outcome\":\"Pending\",\"reads\":13,\"waitedSeconds\":180,\"lastRead\":{\"repo\":\"credfeto/credfeto-workflow-mcp\",\"sha\":\""
        + SHA
        + "\",\"verdict\":\"Passed\",\"passed\":1,\"failed\":0,\"pending\":0,\"notReported\":0,\"runningCheckRuns\":0,\"checks\":["
        + "{\"name\":\"build\",\"verdict\":\"Passed\",\"required\":true,\"reported\":true,\"kind\":\"CheckRun\"}]}}";

    [Fact]
    public static void AnUnconfirmedPassAtTheLimitSerialisesAsPendingWithTheLastRead()
    {
        PrChecksWaitResponse response = new(
            Outcome: PrChecksWaitOutcome.Pending,
            Reads: 13,
            WaitedSeconds: 180,
            LastRead: new(
                Repo: "credfeto/credfeto-workflow-mcp",
                Sha: SHA,
                Verdict: PrChecksVerdict.Passed,
                Passed: 1,
                Failed: 0,
                Pending: 0,
                NotReported: 0,
                RunningCheckRuns: 0,
                Checks:
                [
                    new(
                        Name: "build",
                        Verdict: PrChecksVerdict.Passed,
                        CompletedAt: null,
                        RunId: null,
                        Required: true,
                        Reported: true,
                        Kind: PrCheckKind.CheckRun
                    ),
                ]
            )
        );

        Assert.Equal(
            expected: JSON,
            actual: JsonSerializer.Serialize(value: response, jsonTypeInfo: AppJsonContext.Default.PrChecksWaitResponse)
        );
    }
}
