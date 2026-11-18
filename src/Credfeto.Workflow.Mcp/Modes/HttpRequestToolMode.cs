using Microsoft.AspNetCore.Http;

namespace Credfeto.Workflow.Mcp.Modes;

public sealed class HttpRequestToolMode : IRequestToolMode
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpRequestToolMode(IHttpContextAccessor httpContextAccessor)
    {
        this._httpContextAccessor = httpContextAccessor;
    }

    public ToolModes Current => RequestedToolMode.Of(this._httpContextAccessor.HttpContext);
}
