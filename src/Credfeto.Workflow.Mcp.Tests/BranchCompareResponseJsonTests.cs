using System.Text.Json;
using Credfeto.Workflow.Data.Models.Json;
using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class BranchCompareResponseJsonTests : TestBase
{
    private const string JSON =
        "{\"repo\":\"credfeto/credfeto-workflow-mcp\",\"base\":\"main\",\"head\":\"feat/x\",\"status\":\"behind\",\"aheadBy\":0,\"behindBy\":3}";

    [Theory]
    [InlineData(BranchCompareStatus.Identical, "identical")]
    [InlineData(BranchCompareStatus.Ahead, "ahead")]
    [InlineData(BranchCompareStatus.Behind, "behind")]
    [InlineData(BranchCompareStatus.Diverged, "diverged")]
    public static void TheStatusSerialisesInLowerCase(BranchCompareStatus status, string expected)
    {
        Assert.Equal(
            expected: "\"" + expected + "\"",
            actual: JsonSerializer.Serialize(value: status, jsonTypeInfo: AppJsonContext.Default.BranchCompareStatus)
        );
    }

    [Fact]
    public static void AResponseSerialises()
    {
        BranchCompareResponse response = new(
            Repo: "credfeto/credfeto-workflow-mcp",
            Base: "main",
            Head: "feat/x",
            Status: BranchCompareStatus.Behind,
            AheadBy: 0,
            BehindBy: 3
        );

        Assert.Equal(
            expected: JSON,
            actual: JsonSerializer.Serialize(
                value: response,
                jsonTypeInfo: AppJsonContext.Default.BranchCompareResponse
            )
        );
    }
}
