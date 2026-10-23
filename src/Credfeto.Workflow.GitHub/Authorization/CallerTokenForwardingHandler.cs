using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Authorization.LoggingExtensions;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Credfeto.Workflow.GitHub.Authorization;

public sealed class CallerTokenForwardingHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<CallerTokenForwardingHandler> _logger;

    public CallerTokenForwardingHandler(
        IHttpContextAccessor httpContextAccessor,
        ILogger<CallerTokenForwardingHandler> logger
    )
    {
        this._httpContextAccessor = httpContextAccessor;
        this._logger = logger;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        HttpContext? httpContext = this._httpContextAccessor.HttpContext;
        string? token = httpContext is null ? null : CallerBearerToken.FromRequest(httpContext.Request);

        if (token is null)
        {
            // An unauthenticated upstream call would silently use GitHub's anonymous rate limit and visibility
            this._logger.LogCallerTokenMissing(method: request.Method.Method, requestUri: request.RequestUri);

            throw new CallerTokenMissingException();
        }

        request.Headers.Authorization = new AuthenticationHeaderValue(
            scheme: CallerBearerToken.Scheme,
            parameter: token
        );

        return base.SendAsync(request: request, cancellationToken: cancellationToken);
    }
}
