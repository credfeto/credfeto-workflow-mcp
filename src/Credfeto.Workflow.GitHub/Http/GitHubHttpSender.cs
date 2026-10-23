using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Polly;

namespace Credfeto.Workflow.GitHub.Http;

internal static class GitHubHttpSender
{
    // The standard resilience handler fails a request it gives up on (an attempt or total timeout, a rejected
    // permit, an open circuit) with a Polly ExecutionRejectedException rather than an HttpRequestException, so
    // it is mapped here to keep the clients' failure contract. A cancellation by the caller is not a rejection
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
    }
}
