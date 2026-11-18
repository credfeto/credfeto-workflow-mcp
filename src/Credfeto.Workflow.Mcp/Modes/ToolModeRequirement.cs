using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Credfeto.Workflow.Mcp.Modes;

public static class ToolModeRequirement
{
    // Fixed text: the rejected value is never echoed back
    public const string RejectionBody =
        "{\"jsonrpc\":\"2.0\",\"id\":null,\"error\":{\"code\":-32600,\"message\":\"The "
        + ToolModeHeader.Name
        + " header must be one of: "
        + ToolModeHeader.ModeNames
        + " (in any case), or be left out for every tool\"}}";

    public static void Apply(EndpointBuilder endpoint)
    {
        RequestDelegate handler = endpoint.RequestDelegate
            ?? throw new InvalidOperationException(
                "The MCP endpoint has no request delegate, so the tool mode requirement cannot be applied."
            );

        endpoint.RequestDelegate = context => InvokeAsync(context: context, next: handler);
    }

    public static Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (ToolModeHeader.TryRead(request: context.Request, out ToolModes mode))
        {
            RequestedToolMode.Set(context: context, mode: mode);

            return next(context);
        }

        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.ContentType = "application/json";

        return context.Response.WriteAsync(text: RejectionBody, cancellationToken: context.RequestAborted);
    }
}
