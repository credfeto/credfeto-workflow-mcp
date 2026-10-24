using Microsoft.Extensions.Logging;

namespace Credfeto.Workflow.GitHub.Http.LoggingExtensions;

internal static partial class GitHubRestClientLoggingExtensions
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "GitHub REST request to {Path} failed with status {StatusCode}"
    )]
    public static partial void LogRequestFailed(this ILogger<GitHubRestClient> logger, string path, int statusCode);
}
