using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Authorization;
using Microsoft.AspNetCore.Http;

namespace Credfeto.Workflow.Mcp;

public static class CallerTokenRequirement
{
    public static Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (CallerIdentity.TryResolve(context))
        {
            return next(context);
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = CallerBearerToken.Scheme;

        return Task.CompletedTask;
    }
}
