using Credfeto.Workflow.GitHub.Authorization;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using FunFair.Test.Common;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Authorization;

public sealed class CallerBearerTokenTests : TestBase
{
    [Theory]
    [InlineData("Bearer abc123", "abc123")]
    [InlineData("bearer abc123", "abc123")]
    [InlineData("BEARER abc123", "abc123")]
    public static void BearerTokenIsReturned(string authorization, string expected)
    {
        string? token = CallerBearerToken.FromRequest(CallerContext.WithAuthorization(authorization).Request);

        Assert.Equal(expected: expected, actual: token);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer")]
    [InlineData("Bearer ")]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("token abc123")]
    public static void MissingOrNonBearerAuthorizationReturnsNull(string? authorization)
    {
        string? token = CallerBearerToken.FromRequest(CallerContext.WithAuthorization(authorization).Request);

        Assert.Null(token);
    }

    [Fact]
    public void MultipleAuthorizationHeadersReturnNull()
    {
        DefaultHttpContext context = new();
        context.Request.Headers.Authorization = new(["Bearer first", "Bearer second"]);

        string? token = CallerBearerToken.FromRequest(context.Request);

        Assert.Null(token);
    }
}
