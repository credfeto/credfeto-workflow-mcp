using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Auth;
using Credfeto.Workflow.GitHub.Configuration;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Tests.Helpers;
using Credfeto.Workflow.GitHub.Tests.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Auth;

public sealed class AuthStatusServiceTests : GitHubClientTestBase
{
    private const string LOGIN = "credfeto";
    private const string HOST = "mcp.example";
    private const string USER_JSON = "{\"login\":\"" + LOGIN + "\",\"id\":1}";
    private const string NOT_AUTHENTICATED = "not authenticated";
    private const string UNEXPECTED_USER = "unexpected result for the authenticated user";
    private const string UNEXPECTED_RATE_LIMIT = "unexpected result for the rate limit";
    private const string CORE_JSON = "{\"limit\":5000,\"used\":10,\"remaining\":4990,\"reset\":1795600800}";
    private const string GRAPHQL_JSON = "{\"limit\":5000,\"used\":1000,\"remaining\":4000,\"reset\":1795604400}";

    private static string RateLimitJson(string core = CORE_JSON, string graphql = GRAPHQL_JSON)
    {
        return "{\"resources\":{\"core\":" + core + ",\"graphql\":" + graphql + "},\"rate\":" + CORE_JSON + "}";
    }

    private static Uri ApiUri(string suffix)
    {
        return new(GitHubServices.ApiBaseUrl + suffix);
    }

    private static HttpResponseMessage Unexpected(int index)
    {
        throw new InvalidOperationException("Unexpected request " + index.ToString(CultureInfo.InvariantCulture));
    }

    private static Uri?[] RequestUris(RecordingHttpMessageHandler upstream)
    {
        return [.. upstream.Requests.Select(request => request.RequestUri)];
    }

    private static HttpContext ContextWithHost(string host)
    {
        DefaultHttpContext context = new();
        context.Request.Host = new(host);

        return context;
    }

    private AuthStatusService CreateService(
        RecordingHttpMessageHandler upstream,
        HttpContext? context = null,
        string? apiVersion = GitHubOptions.DefaultApiVersion
    )
    {
        GitHubRestClient rest = new(
            httpClientFactory: MockHttpClientFactoryCreateClient(upstream),
            options: GitHubOptionsWith(),
            logger: this.GetTypedLogger<GitHubRestClient>()
        );

        IHttpContextAccessor accessor = GetSubstitute<IHttpContextAccessor>();
        accessor.HttpContext.Returns(context);

        return new(
            restClient: rest,
            options: Options.Create(new GitHubOptions { ApiVersion = apiVersion }),
            httpContextAccessor: accessor
        );
    }

    private static RecordingHttpMessageHandler UserThen(Func<HttpResponseMessage> rateLimit)
    {
        return new(index =>
            index switch
            {
                0 => Responses.Json(USER_JSON),
                1 => rateLimit(),
                _ => Unexpected(index),
            }
        );
    }

    private static RecordingHttpMessageHandler UserOnly(Func<HttpResponseMessage> user)
    {
        return new(index => index == 0 ? user() : Unexpected(index));
    }

    private static RecordingHttpMessageHandler UserOnly()
    {
        return UserOnly(() => Responses.Json(USER_JSON));
    }

    private Task<AuthStatusFailedException> FailsAsync(RecordingHttpMessageHandler upstream)
    {
        AuthStatusService service = this.CreateService(upstream);

        return Assert.ThrowsAsync<AuthStatusFailedException>(() =>
            service.ReadAsync(rateLimit: true, cancellationToken: this.CancellationToken()).AsTask()
        );
    }

    [Fact]
    public async Task TheLoginIsReadWithoutTheRateLimitsAsync()
    {
        using RecordingHttpMessageHandler upstream = UserOnly();
        AuthStatusService service = this.CreateService(upstream: upstream, context: ContextWithHost(HOST + ":8443"));

        AuthStatusResponse status = await service.ReadAsync(
            rateLimit: false,
            cancellationToken: this.CancellationToken()
        );

        Assert.Equal(
            expected: new AuthStatusResponse(
                Login: LOGIN,
                Host: HOST,
                Version: GitHubOptions.DefaultApiVersion,
                RateLimits: null
            ),
            actual: status
        );
        Assert.Equal(expected: [ApiUri("user")], actual: RequestUris(upstream));
    }

    [Fact]
    public async Task TheRateLimitsAreReadWhenAskedForAsync()
    {
        using RecordingHttpMessageHandler upstream = UserThen(() => Responses.Json(RateLimitJson()));
        AuthStatusService service = this.CreateService(upstream: upstream, context: ContextWithHost(HOST));

        AuthStatusResponse status = await service.ReadAsync(
            rateLimit: true,
            cancellationToken: this.CancellationToken()
        );

        Assert.Equal(expected: LOGIN, actual: status.Login);
        Assert.Equal(expected: HOST, actual: status.Host);
        Assert.Equal(expected: GitHubOptions.DefaultApiVersion, actual: status.Version);
        Assert.NotNull(status.RateLimits);
        Assert.Equal(
            expected: new AuthRateLimit(Remaining: 4990, Limit: 5000, Reset: "2026-11-25T10:00:00Z"),
            actual: status.RateLimits.Core
        );
        Assert.Equal(
            expected: new AuthRateLimit(Remaining: 4000, Limit: 5000, Reset: "2026-11-25T11:00:00Z"),
            actual: status.RateLimits.Graphql
        );
        Assert.Equal(expected: [ApiUri("user"), ApiUri("rate_limit")], actual: RequestUris(upstream));
    }

    [Theory]
    [InlineData("credfeto")]
    [InlineData("dependabot[bot]")]
    [InlineData("a-b_c-1")]
    public async Task AValidLoginIsReturnedAsync(string login)
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0 ? Responses.Json("{\"login\":\"" + login + "\"}") : Unexpected(index)
        );
        AuthStatusService service = this.CreateService(upstream);

        AuthStatusResponse status = await service.ReadAsync(
            rateLimit: false,
            cancellationToken: this.CancellationToken()
        );

        Assert.Equal(expected: login, actual: status.Login);
    }

    [Theory]
    [InlineData("{\"login\":\"\"}")]
    [InlineData("{\"login\":null}")]
    [InlineData("{}")]
    [InlineData("{\"login\":\"bad login\"}")]
    [InlineData("{\"login\":\"bot[bot][bot]\"}")]
    [InlineData("{\"login\":\"credfeto\\n\"}")]
    [InlineData("{\"login\":\"crédfeto\"}")]
    public async Task AnInvalidLoginIsAnUnexpectedResultAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = UserOnly(() => Responses.Json(json));
        AuthStatusFailedException exception = await this.FailsAsync(upstream);

        Assert.Equal(expected: UNEXPECTED_USER, actual: exception.Message);
    }

    [Fact]
    public async Task AnUnreadableUserIsAFailureAsync()
    {
        using RecordingHttpMessageHandler upstream = UserOnly(() => Responses.Json("not json"));
        AuthStatusFailedException exception = await this.FailsAsync(upstream);

        Assert.Equal(
            expected: "could not read the authenticated user: the answer from GitHub could not be read",
            actual: exception.Message
        );
        Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "403")]
    [InlineData(HttpStatusCode.NotFound, "404")]
    [InlineData(HttpStatusCode.InternalServerError, "500")]
    public async Task AFailedUserReadIsReportedAsync(HttpStatusCode statusCode, string code)
    {
        using RecordingHttpMessageHandler upstream = UserOnly(() => Responses.Status(statusCode));
        AuthStatusFailedException exception = await this.FailsAsync(upstream);

        Assert.Equal(
            expected: "could not read the authenticated user: GitHub answered with status " + code,
            actual: exception.Message
        );
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task AUserRequestThatFailsIsReportedAsync()
    {
        using RecordingHttpMessageHandler upstream = UserOnly(() =>
            throw new HttpRequestException("connection refused")
        );
        AuthStatusFailedException exception = await this.FailsAsync(upstream);

        Assert.Equal(
            expected: "could not read the authenticated user: the request to GitHub failed",
            actual: exception.Message
        );
    }

    [Fact]
    public async Task AnUnauthorisedUserReadIsNotAuthenticatedAsync()
    {
        using RecordingHttpMessageHandler upstream = new(index =>
            index == 0 ? Responses.Status(HttpStatusCode.Unauthorized) : Unexpected(index)
        );
        AuthStatusService service = this.CreateService(upstream);

        NotAuthenticatedException exception = await Assert.ThrowsAsync<NotAuthenticatedException>(() =>
            service.ReadAsync(rateLimit: true, cancellationToken: this.CancellationToken()).AsTask()
        );

        Assert.Equal(expected: NOT_AUTHENTICATED, actual: exception.Message);
        Assert.Equal(expected: [ApiUri("user")], actual: RequestUris(upstream));
    }

    [Fact]
    public async Task AMissingCallerTokenIsNotAuthenticatedAsync()
    {
        CallerTokenMissingException missing = new();
        using RecordingHttpMessageHandler upstream = new(_ => throw missing);
        AuthStatusService service = this.CreateService(upstream);

        NotAuthenticatedException exception = await Assert.ThrowsAsync<NotAuthenticatedException>(() =>
            service.ReadAsync(rateLimit: false, cancellationToken: this.CancellationToken()).AsTask()
        );

        Assert.Equal(expected: NOT_AUTHENTICATED, actual: exception.Message);
        Assert.Same(expected: missing, actual: exception.InnerException);
    }

    [Fact]
    public async Task AnUnauthorisedRateLimitReadIsNotAuthenticatedAsync()
    {
        using RecordingHttpMessageHandler upstream = UserThen(() => Responses.Status(HttpStatusCode.Unauthorized));
        AuthStatusService service = this.CreateService(upstream);

        NotAuthenticatedException exception = await Assert.ThrowsAsync<NotAuthenticatedException>(() =>
            service.ReadAsync(rateLimit: true, cancellationToken: this.CancellationToken()).AsTask()
        );

        Assert.Equal(expected: NOT_AUTHENTICATED, actual: exception.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "403")]
    [InlineData(HttpStatusCode.InternalServerError, "500")]
    public async Task AFailedRateLimitReadIsReportedAsync(HttpStatusCode statusCode, string code)
    {
        using RecordingHttpMessageHandler upstream = UserThen(() => Responses.Status(statusCode));
        AuthStatusFailedException exception = await this.FailsAsync(upstream);

        Assert.Equal(
            expected: "could not read the rate limit: GitHub answered with status " + code,
            actual: exception.Message
        );
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData(
        "{\"resources\":{\"core\":{\"limit\":\"5000\",\"remaining\":1,\"reset\":1},\"graphql\":" + GRAPHQL_JSON + "}}"
    )]
    [InlineData(
        "{\"resources\":{\"core\":{\"limit\":5000,\"remaining\":1.5,\"reset\":1},\"graphql\":" + GRAPHQL_JSON + "}}"
    )]
    public async Task AnUnreadableRateLimitIsAnUnexpectedResultAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = UserThen(() => Responses.Json(json));
        AuthStatusFailedException exception = await this.FailsAsync(upstream);

        Assert.Equal(expected: UNEXPECTED_RATE_LIMIT, actual: exception.Message);
        Assert.IsType<GitHubInvalidResponseException>(exception.InnerException);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"resources\":null}")]
    [InlineData("{\"resources\":{\"graphql\":" + GRAPHQL_JSON + "}}")]
    [InlineData("{\"resources\":{\"core\":" + CORE_JSON + "}}")]
    public async Task MissingRateLimitsAreAnUnexpectedResultAsync(string json)
    {
        using RecordingHttpMessageHandler upstream = UserThen(() => Responses.Json(json));
        AuthStatusFailedException exception = await this.FailsAsync(upstream);

        Assert.Equal(expected: UNEXPECTED_RATE_LIMIT, actual: exception.Message);
    }

    [Theory]
    [InlineData("{\"limit\":5000,\"reset\":1}")]
    [InlineData("{\"remaining\":1,\"reset\":1}")]
    [InlineData("{\"remaining\":1,\"limit\":5000}")]
    [InlineData("{\"remaining\":-1,\"limit\":5000,\"reset\":1}")]
    [InlineData("{\"remaining\":1,\"limit\":-1,\"reset\":1}")]
    [InlineData("{\"remaining\":1,\"limit\":5000,\"reset\":-1}")]
    [InlineData("{\"remaining\":1,\"limit\":5000,\"reset\":253402300800}")]
    public async Task InvalidRateLimitValuesAreAnUnexpectedResultAsync(string resource)
    {
        using RecordingHttpMessageHandler coreUpstream = UserThen(() => Responses.Json(RateLimitJson(core: resource)));
        AuthStatusFailedException coreException = await this.FailsAsync(coreUpstream);
        using RecordingHttpMessageHandler graphqlUpstream = UserThen(() =>
            Responses.Json(RateLimitJson(graphql: resource))
        );
        AuthStatusFailedException graphqlException = await this.FailsAsync(graphqlUpstream);

        Assert.Equal(expected: UNEXPECTED_RATE_LIMIT, actual: coreException.Message);
        Assert.Equal(expected: UNEXPECTED_RATE_LIMIT, actual: graphqlException.Message);
    }

    [Theory]
    [InlineData(0L, "1970-01-01T00:00:00Z")]
    [InlineData(253402300799L, "9999-12-31T23:59:59Z")]
    public async Task TheResetIsGivenAsAUtcTimeAsync(long reset, string expected)
    {
        string resource =
            "{\"remaining\":0,\"limit\":0,\"reset\":" + reset.ToString(CultureInfo.InvariantCulture) + "}";
        using RecordingHttpMessageHandler upstream = UserThen(() =>
            Responses.Json(RateLimitJson(core: resource, graphql: resource))
        );
        AuthStatusService service = this.CreateService(upstream);

        AuthStatusResponse status = await service.ReadAsync(
            rateLimit: true,
            cancellationToken: this.CancellationToken()
        );

        Assert.Equal(
            expected: new AuthRateLimit(Remaining: 0, Limit: 0, Reset: expected),
            actual: status.RateLimits?.Core
        );
        Assert.Equal(
            expected: new AuthRateLimit(Remaining: 0, Limit: 0, Reset: expected),
            actual: status.RateLimits?.Graphql
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnUnsetApiVersionIsReportedAsEmptyAsync(string? apiVersion)
    {
        using RecordingHttpMessageHandler upstream = UserOnly();
        AuthStatusService service = this.CreateService(upstream: upstream, apiVersion: apiVersion);

        AuthStatusResponse status = await service.ReadAsync(
            rateLimit: false,
            cancellationToken: this.CancellationToken()
        );

        Assert.Equal(expected: string.Empty, actual: status.Version);
    }

    [Fact]
    public async Task TheApiVersionIsTrimmedAsync()
    {
        using RecordingHttpMessageHandler upstream = UserOnly();
        AuthStatusService service = this.CreateService(upstream: upstream, apiVersion: " 2022-11-28 ");

        AuthStatusResponse status = await service.ReadAsync(
            rateLimit: false,
            cancellationToken: this.CancellationToken()
        );

        Assert.Equal(expected: "2022-11-28", actual: status.Version);
    }

    [Theory]
    [InlineData("[::1]:8080", "[::1]")]
    [InlineData("127.0.0.1", "127.0.0.1")]
    [InlineData("MCP-1.example", "MCP-1.example")]
    [InlineData("bad_host", "")]
    [InlineData("", "")]
    public async Task TheHostIsOnlyGivenWhenItIsAValidHostNameAsync(string host, string expected)
    {
        using RecordingHttpMessageHandler upstream = UserOnly();
        AuthStatusService service = this.CreateService(upstream: upstream, context: ContextWithHost(host));

        AuthStatusResponse status = await service.ReadAsync(
            rateLimit: false,
            cancellationToken: this.CancellationToken()
        );

        Assert.Equal(expected: expected, actual: status.Host);
    }

    [Fact]
    public async Task ATooLongHostIsNotGivenAsync()
    {
        using RecordingHttpMessageHandler upstream = UserOnly();
        AuthStatusService service = this.CreateService(
            upstream: upstream,
            context: ContextWithHost(new string(c: 'a', count: 256))
        );

        AuthStatusResponse status = await service.ReadAsync(
            rateLimit: false,
            cancellationToken: this.CancellationToken()
        );

        Assert.Equal(expected: string.Empty, actual: status.Host);
    }

    [Fact]
    public async Task NoRequestMeansNoHostAsync()
    {
        using RecordingHttpMessageHandler upstream = UserOnly();
        AuthStatusService service = this.CreateService(upstream);

        AuthStatusResponse status = await service.ReadAsync(
            rateLimit: false,
            cancellationToken: this.CancellationToken()
        );

        Assert.Equal(expected: string.Empty, actual: status.Host);
    }
}
