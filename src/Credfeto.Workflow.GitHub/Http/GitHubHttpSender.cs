using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Polly;

namespace Credfeto.Workflow.GitHub.Http;

internal static class GitHubHttpSender
{
    // The resilience handler fails a request that runs out of time (an attempt or total request timeout) with a
    // Polly ExecutionRejectedException rather than an HttpRequestException, so it is mapped here to keep the
    // clients' failure contract. The resilience handler only bounds the wait for the response headers; buffering
    // the body is bounded by HttpClient's own timeout, which surfaces as an OperationCanceledException although
    // the caller did not cancel, so that is mapped the same way. A cancellation by the caller is not a rejection
    // and still surfaces as OperationCanceledException
    public static async ValueTask<HttpResponseMessage> SendAsync(
        HttpClient httpClient,
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await httpClient.SendAsync(request: request, cancellationToken: cancellationToken);
        }
        catch (ExecutionRejectedException exception)
        {
            throw GitHubHttpFailure.FromRejection(exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw GitHubHttpFailure.FromRejection(exception);
        }
    }
}
