using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Credfeto.Workflow.Mcp.Modes;
using FunFair.Test.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests.Modes;

public sealed class ToolModeRequirementTests : TestBase
{
    [Fact]
    public static void ApplyWithoutRequestDelegateFailsClosed()
    {
        RouteEndpointBuilder endpoint = new(
            requestDelegate: null,
            routePattern: RoutePatternFactory.Parse("/mcp"),
            order: 0
        );

        _ = Assert.Throws<InvalidOperationException>(() => ToolModeRequirement.Apply(endpoint));
    }

    [Fact]
    public static async Task ApplyWrapsRequestDelegateWithModeRequirementAsync()
    {
        bool nextCalled = false;
        RouteEndpointBuilder endpoint = new(
            requestDelegate: _ =>
            {
                nextCalled = true;

                return Task.CompletedTask;
            },
            routePattern: RoutePatternFactory.Parse("/mcp"),
            order: 0
        );

        ToolModeRequirement.Apply(endpoint);

        DefaultHttpContext context = new();
        context.Request.Headers[ToolModeHeader.Name] = "Unknown";

        RequestDelegate? wrapped = endpoint.RequestDelegate;
        Assert.NotNull(wrapped);

        await wrapped(context);

        Assert.False(condition: nextCalled, userMessage: "Next should not be called for an unknown mode");
        Assert.Equal(expected: StatusCodes.Status400BadRequest, actual: context.Response.StatusCode);
    }

    [Theory]
    [InlineData(null, ToolModes.All)]
    [InlineData("Issues", ToolModes.Issues)]
    [InlineData("issues", ToolModes.Issues)]
    [InlineData("ALL", ToolModes.All)]
    public static async Task AKnownModeReachesNextWithTheModeSetAsync(string? header, ToolModes expected)
    {
        DefaultHttpContext context = new();

        if (header is not null)
        {
            context.Request.Headers[ToolModeHeader.Name] = header;
        }

        ToolModes seen = ToolModes.None;

        await ToolModeRequirement.InvokeAsync(context: context, next: reached =>
        {
            seen = RequestedToolMode.Of(reached);

            return Task.CompletedTask;
        });

        Assert.Equal(expected: expected, actual: seen);
        Assert.Equal(expected: StatusCodes.Status200OK, actual: context.Response.StatusCode);
    }

    [Fact]
    public static async Task AnUnknownModeIsRejectedWithAJsonRpcErrorAsync()
    {
        DefaultHttpContext context = new();
        context.Request.Headers[ToolModeHeader.Name] = "Isues";
        await using MemoryStream body = new();
        context.Response.Body = body;

        bool nextCalled = false;

        await ToolModeRequirement.InvokeAsync(context: context, next: _ =>
        {
            nextCalled = true;

            return Task.CompletedTask;
        });

        string written = Encoding.UTF8.GetString(body.ToArray());

        Assert.False(condition: nextCalled, userMessage: "Next should not be called for an unknown mode");
        Assert.Equal(expected: StatusCodes.Status400BadRequest, actual: context.Response.StatusCode);
        Assert.Equal(expected: "application/json", actual: context.Response.ContentType);
        Assert.Equal(expected: ToolModeRequirement.RejectionBody, actual: written);
        Assert.Contains(
            expectedSubstring: "X-Mode header must be one of: All, Issues",
            actualString: written,
            comparisonType: StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            expectedSubstring: "Isues",
            actualString: written,
            comparisonType: StringComparison.Ordinal
        );
    }
}
