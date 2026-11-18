using System;
using Credfeto.Workflow.Mcp.Modes;
using FunFair.Test.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests.Modes;

public sealed class ToolModeHeaderTests : TestBase
{
    [Fact]
    public static void NoHeaderIsEveryTool()
    {
        DefaultHttpContext context = new();

        Assert.True(
            ToolModeHeader.TryRead(request: context.Request, out ToolModes mode),
            userMessage: "no header is valid"
        );
        Assert.Equal(expected: ToolModes.All, actual: mode);
    }

    [Theory]
    [InlineData("Issues", ToolModes.Issues)]
    [InlineData("issues", ToolModes.Issues)]
    [InlineData("ISSUES", ToolModes.Issues)]
    [InlineData(" iSsUeS ", ToolModes.Issues)]
    [InlineData("All", ToolModes.All)]
    [InlineData("all", ToolModes.All)]
    public static void KnownModeNamesMatchInAnyCase(string value, ToolModes expected)
    {
        DefaultHttpContext context = new();
        context.Request.Headers[ToolModeHeader.Name] = value;

        Assert.True(
            ToolModeHeader.TryRead(request: context.Request, out ToolModes mode),
            userMessage: value + " is a mode"
        );
        Assert.Equal(expected: expected, actual: mode);
    }

    [Theory]
    [InlineData("Issue")]
    [InlineData("Unknown")]
    [InlineData("None")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("2")]
    [InlineData("Issues, All")]
    [InlineData("Issues,All")]
    public static void AnythingElseIsRefused(string value)
    {
        DefaultHttpContext context = new();
        context.Request.Headers[ToolModeHeader.Name] = value;

        Assert.False(
            ToolModeHeader.TryRead(request: context.Request, out ToolModes mode),
            userMessage: value + " is not a mode"
        );
        Assert.Equal(expected: ToolModes.None, actual: mode);
    }

    [Fact]
    public static void MoreThanOneHeaderIsRefused()
    {
        DefaultHttpContext context = new();
        context.Request.Headers[ToolModeHeader.Name] = new StringValues(["Issues", "Issues"]);

        Assert.False(
            ToolModeHeader.TryRead(request: context.Request, out ToolModes mode),
            userMessage: "two headers are refused"
        );
        Assert.Equal(expected: ToolModes.None, actual: mode);
    }

    [Fact]
    public static void ANullHeaderValueIsRefused()
    {
        DefaultHttpContext context = new();
        string?[] values = [null];
        context.Request.Headers[ToolModeHeader.Name] = new StringValues(values);

        Assert.False(
            ToolModeHeader.TryRead(request: context.Request, out ToolModes mode),
            userMessage: "a null value is refused"
        );
        Assert.Equal(expected: ToolModes.None, actual: mode);
    }

    [Theory]
    [InlineData(ToolModes.All, "All")]
    [InlineData(ToolModes.Issues, "Issues")]
    public static void ModesHaveNames(ToolModes mode, string expected)
    {
        Assert.Equal(expected: expected, actual: ToolModeHeader.NameOf(mode));
    }

    [Fact]
    public static void NoneHasNoName()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => ToolModeHeader.NameOf(ToolModes.None));
    }
}
