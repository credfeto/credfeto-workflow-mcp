using System.Threading.Tasks;
using FunFair.Test.Common;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class CallerTokenRequirementTests : TestBase
{
    [Theory]
    [InlineData("Bearer caller-token", true, StatusCodes.Status200OK, "")]
    [InlineData(null, false, StatusCodes.Status401Unauthorized, "Bearer")]
    [InlineData("Basic dXNlcjpwYXNz", false, StatusCodes.Status401Unauthorized, "Bearer")]
    [InlineData("Bearer ", false, StatusCodes.Status401Unauthorized, "Bearer")]
    public static async Task OnlyBearerTokenReachesNextAsync(
        string? authorization,
        bool expectedNextCalled,
        int expectedStatusCode,
        string expectedChallenge
    )
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

        Assert.Equal(expected: expectedNextCalled, actual: nextCalled);
        Assert.Equal(expected: expectedStatusCode, actual: context.Response.StatusCode);
        Assert.Equal(expected: expectedChallenge, actual: context.Response.Headers.WWWAuthenticate.ToString());
    }
}
