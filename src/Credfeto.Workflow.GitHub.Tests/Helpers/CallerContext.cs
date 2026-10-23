using Microsoft.AspNetCore.Http;

namespace Credfeto.Workflow.GitHub.Tests.Helpers;

internal static class CallerContext
{
    public const string Token = "caller-token-value";

    public static HttpContext WithAuthorization(string? authorization)
    {
        DefaultHttpContext context = new();

        if (authorization is not null)
        {
            context.Request.Headers.Authorization = authorization;
        }

        return context;
    }

    public static HttpContext WithBearerToken()
    {
        return WithAuthorization("Bearer " + Token);
    }
}
