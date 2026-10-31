using Microsoft.Extensions.Logging;

namespace Credfeto.Workflow.GitHub.Board.LoggingExtensions;

internal static partial class WorkflowBoardReaderLoggingExtensions
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Board status of {Reference}: the direct GraphQL read failed ({Reason}); falling back to listing the board"
    )]
    public static partial void LogDirectReadFailed(
        this ILogger<WorkflowBoardReader> logger,
        string reference,
        string reason
    );
}
