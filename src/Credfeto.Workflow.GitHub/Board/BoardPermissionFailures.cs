using System;
using System.Linq;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;

namespace Credfeto.Workflow.GitHub.Board;

// Decides when the caller's token was refused the Workflow board, which is the only case the board token is used for
public static class BoardPermissionFailures
{
    private const string FORBIDDEN = "FORBIDDEN";

    private const string INSUFFICIENT_SCOPES = "INSUFFICIENT_SCOPES";

    // GitHub reports a node the token cannot see as one that does not exist
    private const string COULD_NOT_RESOLVE = "Could not resolve";

    public static bool IsPermissionFailure(Exception exception)
    {
        return exception switch
        {
            // A board the caller cannot see is left out of the repository's projects rather than reported
            WorkflowBoardNotFoundException => true,
            GitHubGraphQlException graphQl => IsRefused(graphQl),
            WorkflowBoardInvalidException or StatusFieldReadFailedException or BoardWriteException =>
                exception.InnerException is GitHubGraphQlException inner && IsRefused(inner),
            _ => false,
        };
    }

    private static bool IsRefused(GitHubGraphQlException exception)
    {
        return exception.ErrorTypes.Any(static type =>
                StringComparer.Ordinal.Equals(x: type, y: FORBIDDEN)
                || StringComparer.Ordinal.Equals(x: type, y: INSUFFICIENT_SCOPES)
            )
            || exception.Errors.Any(static error =>
                error.Contains(value: COULD_NOT_RESOLVE, comparisonType: StringComparison.Ordinal)
            );
    }
}
