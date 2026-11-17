using System;
using Microsoft.Extensions.Logging;

namespace Credfeto.Workflow.GitHub.Board.LoggingExtensions;

// The board token itself is never logged, only that it was used and for what
internal static partial class BoardFallbackWriterLoggingExtensions
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Board token: resolved the Workflow board of {Repository} because the caller's token could not"
    )]
    public static partial void LogResolvedWithBoardToken(this ILogger<BoardFallbackWriter> logger, string repository);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "Board token: added {Url} to project {ProjectNumber} as Not Started because the caller's token could not"
    )]
    public static partial void LogAddedWithBoardToken(
        this ILogger<BoardFallbackWriter> logger,
        Uri url,
        int projectNumber
    );
}
