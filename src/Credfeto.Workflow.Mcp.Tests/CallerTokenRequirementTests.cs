using System;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Authorization;
using FunFair.Test.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Xunit;

namespace Credfeto.Workflow.Mcp.Tests;

public sealed class CallerTokenRequirementTests : TestBase
{
    [Fact]
    public static void ApplyWithoutRequestDelegateFailsClosed()
    {
        RouteEndpointBuilder endpoint = new(
            requestDelegate: null,
            routePattern: RoutePatternFactory.Parse("/mcp"),
            order: 0
        );

        _ = Assert.Throws<InvalidOperationException>(() => CallerTokenRequirement.Apply(endpoint));
    }

    [Fact]
    public static async Task ApplyWrapsRequestDelegateWithTokenRequirementAsync()
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

        CallerTokenRequirement.Apply(endpoint);

        DefaultHttpContext context = new();

        RequestDelegate? wrapped = endpoint.RequestDelegate;
        Assert.NotNull(wrapped);

        await wrapped(context);

        Assert.False(condition: nextCalled, userMessage: "Next should not be called without a caller token");
        Assert.Equal(expected: StatusCodes.Status401Unauthorized, actual: context.Response.StatusCode);
    }

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
        Assert.Equal(expected: expectedNextCalled, actual: CallerIdentity.Of(context) is not null);
        Assert.Equal(expected: expectedStatusCode, actual: context.Response.StatusCode);
        Assert.Equal(expected: expectedChallenge, actual: context.Response.Headers.WWWAuthenticate.ToString());
    }
}
