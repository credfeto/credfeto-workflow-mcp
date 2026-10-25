using Credfeto.Workflow.GitHub.Authorization;
using Microsoft.AspNetCore.Http;

namespace Credfeto.Workflow.GitHub.Tests.Helpers;

internal static class CallerContext
{
    public const string Token = "caller-token-value";

    // Resolves the caller identity as the MCP endpoint's token requirement does before any GitHub call is made
    public static HttpContext WithAuthorization(string? authorization)
    {
        DefaultHttpContext context = new();

        if (authorization is not null)
        {
            context.Request.Headers.Authorization = authorization;
        }

        _ = CallerIdentity.TryResolve(context);

        return context;
    }

    public static HttpContext WithBearerToken()
    {
        return WithAuthorization("Bearer " + Token);
    }
}
