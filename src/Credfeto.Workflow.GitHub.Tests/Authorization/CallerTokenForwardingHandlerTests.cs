using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Authorization;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using FunFair.Test.Common;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Authorization;

public sealed class CallerTokenForwardingHandlerTests : TestBase
{
    private static readonly Uri RequestUri = new("https://github-proxy.example/repos/owner/repo");

    [Fact]
    public async Task CallerBearerTokenIsForwardedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.OK));

        using HttpResponseMessage response = await this.SendAsync(
            httpContext: CallerContext.WithBearerToken(),
            upstream: upstream
        );

        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: "Bearer " + CallerContext.Token, actual: request.Authorization);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(true, "Basic dXNlcjpwYXNz")]
    [InlineData(true, "Bearer ")]
    public async Task MissingOrNonBearerCallerTokenIsNeverSentUpstreamAsync(bool hasHttpContext, string? authorization)
    {
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.OK));
        HttpContext? httpContext = hasHttpContext ? CallerContext.WithAuthorization(authorization) : null;

        await Assert.ThrowsAsync<CallerTokenMissingException>(() =>
            this.SendAsync(httpContext: httpContext, upstream: upstream)
        );

        Assert.Empty(upstream.Requests);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpContext? httpContext, HttpMessageHandler upstream)
    {
        using CallerTokenForwardingHandler handler = new(
            httpContextAccessor: new HttpContextAccessor { HttpContext = httpContext },
            logger: this.GetTypedLogger<CallerTokenForwardingHandler>()
        )
        {
            InnerHandler = upstream,
        };

        using HttpMessageInvoker invoker = new(handler: handler, disposeHandler: false);
        using HttpRequestMessage request = new(method: HttpMethod.Get, requestUri: RequestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue(scheme: "Bearer", parameter: "server-supplied");

        return await invoker.SendAsync(request: request, cancellationToken: this.CancellationToken());
    }
}
