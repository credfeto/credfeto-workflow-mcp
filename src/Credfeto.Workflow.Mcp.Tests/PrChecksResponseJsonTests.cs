using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class PrChecksResponseJsonTests : TestBase
{
    private const string SHA = "0123456789abcdef0123456789abcdef01234567";

    private const string JSON =
        "{\"repo\":\"credfeto/credfeto-workflow-mcp\",\"sha\":\""
        + SHA
        + "\",\"verdict\":\"Pending\",\"passed\":0,\"failed\":0,\"pending\":2,\"notReported\":1,\"runningCheckRuns\":1,\"checks\":["
        + "{\"name\":\"build\",\"verdict\":\"Pending\",\"required\":true,\"reported\":true,\"kind\":\"CheckRun\"},"
        + "{\"name\":\"ci/legacy\",\"verdict\":\"Pending\",\"required\":false,\"reported\":true,\"kind\":\"Status\"},"
        + "{\"name\":\"deploy\",\"verdict\":\"Pending\",\"required\":true,\"reported\":false}]}";

    [Fact]
    public static void AResponseSerialisesWithEachCheckReportedAndKind()
    {
        PrChecksResponse response = new(
            Repo: "credfeto/credfeto-workflow-mcp",
            Sha: SHA,
            Verdict: PrChecksVerdict.Pending,
            Passed: 0,
            Failed: 0,
            Pending: 2,
            NotReported: 1,
            RunningCheckRuns: 1,
            Checks:
            [
                Check(name: "build", required: true, reported: true, kind: PrCheckKind.CheckRun),
                Check(name: "ci/legacy", required: false, reported: true, kind: PrCheckKind.Status),
                Check(name: "deploy", required: true, reported: false, kind: null),
            ]
        );

        Assert.Equal(
            expected: JSON,
            actual: JsonSerializer.Serialize(value: response, jsonTypeInfo: AppJsonContext.Default.PrChecksResponse)
        );
    }

    private static PrCheckResult Check(string name, bool required, bool reported, PrCheckKind? kind)
    {
        return new(
            Name: name,
            Verdict: PrChecksVerdict.Pending,
            CompletedAt: null,
            RunId: null,
            Required: required,
            Reported: reported,
            Kind: kind
        );
    }
}
