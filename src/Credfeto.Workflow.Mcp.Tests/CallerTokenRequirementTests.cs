using System.Threading.Tasks;
using FunFair.Test.Common;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class CallerTokenRequirementTests : TestBase
{
    [Fact]
    public static async Task BearerTokenReachesNextAsync()
    {
        DefaultHttpContext context = new();
        context.Request.Headers.Authorization = "Bearer caller-token";
        bool nextCalled = false;

        await CallerTokenRequirement.InvokeAsync(
            context: context,
            next: _ =>
            {
                nextCalled = true;

                return Task.CompletedTask;
            }
        );

        Assert.True(condition: nextCalled, userMessage: "An authorised request should reach the MCP handler");
        Assert.Equal(expected: StatusCodes.Status200OK, actual: context.Response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Bearer ")]
    public static async Task MissingOrNonBearerTokenIsRejectedAsync(string? authorization)
    {
        DefaultHttpContext context = new();

        if (authorization is not null)
        {
            context.Request.Headers.Authorization = authorization;
        }

        bool nextCalled = false;

        await CallerTokenRequirement.InvokeAsync(
            context: context,
            next: _ =>
            {
                nextCalled = true;

                return Task.CompletedTask;
            }
        );

        Assert.False(condition: nextCalled, userMessage: "An unauthorised request must not reach the MCP handler");
        Assert.Equal(expected: StatusCodes.Status401Unauthorized, actual: context.Response.StatusCode);
        Assert.Equal(expected: "Bearer", actual: context.Response.Headers.WWWAuthenticate.ToString());
    }
}
