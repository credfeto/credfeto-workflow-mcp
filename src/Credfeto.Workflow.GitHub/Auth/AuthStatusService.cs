using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Configuration;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Issues;
using Credfeto.Workflow.GitHub.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Credfeto.Workflow.GitHub.Auth;

public sealed partial class AuthStatusService : IAuthStatusService
{
    private const string NOT_AUTHENTICATED = "not authenticated";
    private const string UNEXPECTED_USER = "unexpected result for the authenticated user";
    private const string UNEXPECTED_RATE_LIMIT = "unexpected result for the rate limit";
    private const int MAX_HOST_LENGTH = 255;
    private const int REGEX_TIMEOUT_MILLISECONDS = 1000;

    // The latest reset a DateTimeOffset can represent, so a larger one is refused rather than overflowing
    private static readonly long MaxResetSeconds = DateTimeOffset.MaxValue.ToUnixTimeSeconds();

    // The Host header comes from the caller, so only the characters of a DNS name, an IPv4 address or a bracketed
    // IPv6 address are passed back
    private static readonly SearchValues<char> HostCharacters = SearchValues.Create(
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789.-:[]"
    );

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IGitHubRestClient _restClient;
    private readonly string _version;

    public AuthStatusService(
        IGitHubRestClient restClient,
        IOptions<GitHubOptions> options,
        IHttpContextAccessor httpContextAccessor
    )
    {
        this._restClient = restClient;
        this._httpContextAccessor = httpContextAccessor;
        this._version = options.Value.ApiVersion?.Trim() ?? string.Empty;
    }

    // Nothing is returned unless every read asked for succeeded, so a caller never sees a partial status
    public async ValueTask<AuthStatusResponse> ReadAsync(bool rateLimit, CancellationToken cancellationToken)
    {
        string login = await this.ReadLoginAsync(cancellationToken);

        AuthRateLimits? rateLimits = rateLimit ? await this.ReadRateLimitsAsync(cancellationToken) : null;

        return new(Login: login, Host: this.McpHost(), Version: this._version, RateLimits: rateLimits);
    }

    private async ValueTask<string> ReadLoginAsync(CancellationToken cancellationToken)
    {
        AuthenticatedUserResponse response;

        try
        {
            response = await this._restClient.GetAsync(
                relativeUri: "user",
                responseTypeInfo: GitHubJsonContext.Default.AuthenticatedUserResponse,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception) when (IsNotAuthenticated(exception))
        {
            throw new NotAuthenticatedException(message: NOT_AUTHENTICATED, innerException: exception);
        }
        catch (Exception exception) when (exception is GitHubInvalidResponseException or HttpRequestException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new AuthStatusFailedException(
                message: "could not read the authenticated user: " + GitHubHttpFailure.Describe(exception),
                innerException: exception
            );
        }

        return response.Login is { } login && LoginPattern().IsMatch(login)
            ? login
            : throw new AuthStatusFailedException(UNEXPECTED_USER);
    }

    private async ValueTask<AuthRateLimits> ReadRateLimitsAsync(CancellationToken cancellationToken)
    {
        RateLimitResponse response;

        try
        {
            response = await this._restClient.GetAsync(
                relativeUri: "rate_limit",
                responseTypeInfo: GitHubJsonContext.Default.RateLimitResponse,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception) when (IsNotAuthenticated(exception))
        {
            throw new NotAuthenticatedException(message: NOT_AUTHENTICATED, innerException: exception);
        }
        catch (HttpRequestException exception)
        {
            throw new AuthStatusFailedException(
                message: "could not read the rate limit: " + GitHubHttpFailure.Describe(exception),
                innerException: exception
            );
        }
        catch (GitHubInvalidResponseException exception)
        {
            // A count that is not a whole number fails to read as one, so it is reported as an unexpected value
            throw new AuthStatusFailedException(message: UNEXPECTED_RATE_LIMIT, innerException: exception);
        }

        return response.Resources is { Core: { } core, Graphql: { } graphql }
            ? new(Core: ValidLimit(core), Graphql: ValidLimit(graphql))
            : throw new AuthStatusFailedException(UNEXPECTED_RATE_LIMIT);
    }

    private static bool IsNotAuthenticated(Exception exception)
    {
        return exception
            is CallerTokenMissingException
                or HttpRequestException { StatusCode: HttpStatusCode.Unauthorized };
    }

    private static AuthRateLimit ValidLimit(RateLimitResourceResponse resource)
    {
        return
            resource is { Remaining: { } remaining and >= 0, Limit: { } limit and >= 0, Reset: { } reset and >= 0 }
            && reset <= MaxResetSeconds
            ? new(Remaining: remaining, Limit: limit, Reset: ResetTimestamp(reset))
            : throw new AuthStatusFailedException(UNEXPECTED_RATE_LIMIT);
    }

    private static string ResetTimestamp(long reset)
    {
        return DateTimeOffset
            .FromUnixTimeSeconds(reset)
            .UtcDateTime.ToString(
                format: IssueCommentsProjector.TIMESTAMP_FORMAT,
                provider: CultureInfo.InvariantCulture
            );
    }

    private string McpHost()
    {
        string? host = this._httpContextAccessor.HttpContext?.Request.Host.Host;

        return IsValidHost(host) ? host : string.Empty;
    }

    private static bool IsValidHost([NotNullWhen(true)] string? host)
    {
        return !string.IsNullOrEmpty(host)
            && host.Length <= MAX_HOST_LENGTH
            && !host.AsSpan().ContainsAnyExcept(HostCharacters);
    }

    [GeneratedRegex(
        pattern: @"^[A-Za-z0-9_-]+(\[bot\])?\z",
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: REGEX_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex LoginPattern();
}
