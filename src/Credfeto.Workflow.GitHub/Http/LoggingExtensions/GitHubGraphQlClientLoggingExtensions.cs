using Microsoft.Extensions.Logging;

namespace Credfeto.Workflow.GitHub.Http.LoggingExtensions;

internal static partial class GitHubGraphQlClientLoggingExtensions
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "GitHub GraphQL request failed with status {StatusCode}"
    )]
    public static partial void LogRequestFailed(this ILogger<GitHubGraphQlClient> logger, int statusCode);
}
