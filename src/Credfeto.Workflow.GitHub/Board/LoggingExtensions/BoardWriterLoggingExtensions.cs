using Microsoft.Extensions.Logging;

namespace Credfeto.Workflow.GitHub.Board.LoggingExtensions;

internal static partial class BoardWriterLoggingExtensions
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Board item of {Reference}: the cached item id was stale ({Reason}); adding it to the board again"
    )]
    public static partial void LogStaleItemId(this ILogger<BoardWriter> logger, string reference, string reason);
}
