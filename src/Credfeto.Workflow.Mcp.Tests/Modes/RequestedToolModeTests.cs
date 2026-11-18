using Credfeto.Workflow.Mcp.Modes;
using FunFair.Test.Common;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests.Modes;

public sealed class RequestedToolModeTests : TestBase
{
    [Fact]
    public static void NoRequestIsEveryTool()
    {
        Assert.Equal(expected: ToolModes.All, actual: RequestedToolMode.Of(null));
    }

    [Fact]
    public static void ARequestWithoutAModeIsEveryTool()
    {
        Assert.Equal(expected: ToolModes.All, actual: RequestedToolMode.Of(new DefaultHttpContext()));
    }

    [Fact]
    public static void TheModeSetOnTheRequestIsRead()
    {
        DefaultHttpContext context = new();
        RequestedToolMode.Set(context: context, mode: ToolModes.Issues);

        Assert.Equal(expected: ToolModes.Issues, actual: RequestedToolMode.Of(context));
    }

    [Fact]
    public void TheCurrentModeIsTheCurrentRequestsMode()
    {
        DefaultHttpContext context = new();
        RequestedToolMode.Set(context: context, mode: ToolModes.Issues);
        IHttpContextAccessor accessor = GetSubstitute<IHttpContextAccessor>();
        accessor.HttpContext.Returns(context);

        HttpRequestToolMode toolMode = new(accessor);

        Assert.Equal(expected: ToolModes.Issues, actual: toolMode.Current);
    }
}
