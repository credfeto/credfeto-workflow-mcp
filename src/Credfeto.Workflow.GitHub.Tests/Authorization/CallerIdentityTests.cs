using System;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Authorization;
using Credfeto.Workflow.GitHub.Cache;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using FunFair.Test.Common;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Authorization;

public sealed class CallerIdentityTests : TestBase
{
    private const string REPOSITORY = "credfeto/credfeto-workflow-mcp";
    private const string OTHER_CALLER_TOKEN = "other-caller-token-value";

    private static readonly Uri RequestUri = new("https://github-proxy.example/repos/owner/repo");

    private static readonly WorkflowProject Project = new(ProjectNodeId: "PVT_project", Number: 7, Owner: "credfeto");

    [Fact]
    public void BearerTokenIsResolvedWithAHashedCacheKey()
    {
        CallerIdentity identity = AssertReallyNotNull(CallerIdentity.Of(CallerContext.WithBearerToken()));

        Assert.Equal(expected: CallerContext.Token, actual: identity.Token);
        Assert.Equal(
            expected: Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CallerContext.Token))),
            actual: identity.CacheKey
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Bearer ")]
    public static void NonBearerAuthorizationResolvesNoIdentity(string? authorization)
    {
        DefaultHttpContext context = new();

        if (authorization is not null)
        {
            context.Request.Headers.Authorization = authorization;
        }

        Assert.False(
            condition: CallerIdentity.TryResolve(context),
            userMessage: "Only a bearer token resolves an identity"
        );
        Assert.Null(CallerIdentity.Of(context));
    }

    [Fact]
    public void NoHttpContextHasNoIdentity()
    {
        Assert.Null(CallerIdentity.Of(null));
    }

    [Fact]
    public void BearerHeaderWithoutAResolvedIdentityIsRejected()
    {
        DefaultHttpContext context = new();
        context.Request.Headers.Authorization = "Bearer " + CallerContext.Token;
        RepositoryProjectCache cache = new(new HttpContextAccessor { HttpContext = context });

        Assert.Throws<CallerTokenMissingException>(() => cache.GetProject(REPOSITORY));
    }

    [Fact]
    public async Task CacheAndForwardingHandlerActForTheSameResolvedCallerAsync()
    {
        HttpContextAccessor httpContextAccessor = new() { HttpContext = CallerContext.WithBearerToken() };
        RepositoryProjectCache cache = new(httpContextAccessor);
        using RecordingHttpMessageHandler upstream = new(static _ => Responses.Status(HttpStatusCode.OK));

        // A header changed after the identity was resolved must move neither the cache bucket nor the forwarded token
        HttpContext httpContext = AssertReallyNotNull(httpContextAccessor.HttpContext);
        httpContext.Request.Headers.Authorization = "Bearer " + OTHER_CALLER_TOKEN;

        cache.SetProject(repository: REPOSITORY, project: Project);
        using HttpResponseMessage response = await this.SendAsync(
            httpContextAccessor: httpContextAccessor,
            upstream: upstream
        );

        RecordedRequest request = Assert.Single(upstream.Requests);
        Assert.Equal(expected: "Bearer " + CallerContext.Token, actual: request.Authorization);

        httpContextAccessor.HttpContext = CallerContext.WithBearerToken();
        Assert.Equal(expected: Project, actual: cache.GetProject(REPOSITORY));

        httpContextAccessor.HttpContext = CallerContext.WithAuthorization("Bearer " + OTHER_CALLER_TOKEN);
        Assert.Null(cache.GetProject(REPOSITORY));
    }

    private async Task<HttpResponseMessage> SendAsync(
        IHttpContextAccessor httpContextAccessor,
        HttpMessageHandler upstream
    )
    {
        using CallerTokenForwardingHandler handler = new(
            httpContextAccessor: httpContextAccessor,
            logger: this.GetTypedLogger<CallerTokenForwardingHandler>()
        )
        {
            InnerHandler = upstream,
        };

        using HttpMessageInvoker invoker = new(handler: handler, disposeHandler: false);
        using HttpRequestMessage request = new(method: HttpMethod.Get, requestUri: RequestUri);

        return await invoker.SendAsync(request: request, cancellationToken: this.CancellationToken());
    }
}
