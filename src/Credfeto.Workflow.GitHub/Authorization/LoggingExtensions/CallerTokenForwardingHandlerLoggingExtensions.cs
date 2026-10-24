using System;
using Microsoft.Extensions.Logging;

namespace Credfeto.Workflow.GitHub.Authorization.LoggingExtensions;

internal static partial class CallerTokenForwardingHandlerLoggingExtensions
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "No caller bearer token for GitHub {Method} request to {RequestUri}; request not sent"
    )]
    public static partial void LogCallerTokenMissing(
        this ILogger<CallerTokenForwardingHandler> logger,
        string method,
        Uri? requestUri
    );
}
