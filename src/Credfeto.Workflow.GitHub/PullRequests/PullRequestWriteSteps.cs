using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;

namespace Credfeto.Workflow.GitHub.PullRequests;

// Runs the pull request writes of a multi-step tool one at a time, so a failure names the step that failed and the
// steps already made, and tells a refused write from one GitHub may have made
internal static class PullRequestWriteSteps
{
    public static async ValueTask<PullRequestWriteFailure?> TryWriteAsync(
        Func<ValueTask> write,
        string step,
        List<string> steps
    )
    {
        try
        {
            await write();
        }
        catch (Exception exception) when (IsWriteFailure(exception))
        {
            return new(Step: step, Exception: exception);
        }

        steps.Add(step);

        return null;
    }

    public static bool IsWriteFailure(Exception exception)
    {
        return exception
            is BoardWriteException
                or GitHubGraphQlException
                or GitHubInvalidResponseException
                or HttpRequestException;
    }

    // A refused request stored nothing; any other failure came after the request was sent, and writes are never
    // retried, so GitHub may have made the change without the answer arriving
    public static bool MayHaveBeenWritten(Exception exception)
    {
        return exception switch
        {
            BoardWriteException { InnerException: { } inner } => MayHaveBeenWritten(inner),
            BoardWriteException => true,
            GitHubGraphQlException => false,
            HttpRequestException { StatusCode: { } statusCode } => !GitHubHttpFailure.IsRejected(statusCode),
            _ => true,
        };
    }

    // Only the status is described, never the exception's message, so no upstream text reaches the caller
    public static string Describe(Exception exception)
    {
        return exception switch
        {
            BoardWriteException { InnerException: { } inner } => Describe(inner),
            BoardWriteException => "the Workflow board did not confirm the change",
            GitHubGraphQlException => "GitHub refused the change",
            _ => GitHubHttpFailure.Describe(exception),
        };
    }
}
