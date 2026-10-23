using Credfeto.Workflow.Data.Models.Models;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class WorkflowMcpToolsTests : TestBase
{
    [Fact]
    public void PingReturnsPong()
    {
        PingResponse response = WorkflowMcpTools.Ping();

        Assert.Equal(expected: "pong", actual: response.Value);
    }
}
