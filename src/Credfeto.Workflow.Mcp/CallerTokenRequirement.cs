using System;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Credfeto.Workflow.Mcp;

public static class CallerTokenRequirement
{
    public static void Apply(EndpointBuilder endpoint)
    {
        RequestDelegate handler =
            endpoint.RequestDelegate
            ?? throw new InvalidOperationException(
                "The MCP endpoint has no request delegate, so the caller token requirement cannot be applied."
            );

        endpoint.RequestDelegate = context => InvokeAsync(context: context, next: handler);
    }

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
