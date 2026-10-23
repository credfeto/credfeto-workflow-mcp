using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Credfeto.Workflow.GitHub.Tests.Helpers;

// Holds every request in flight until the expected number have arrived, so concurrency limits are observable
internal sealed class GatedHttpMessageHandler : HttpMessageHandler
{
    private readonly TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly int _expectedRequests;
    private int _arrived;

    public GatedHttpMessageHandler(int expectedRequests)
    {
        this._expectedRequests = expectedRequests;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        if (Interlocked.Increment(ref this._arrived) == this._expectedRequests)
        {
            this._allArrived.TrySetResult();
        }

        await this._allArrived.Task.WaitAsync(cancellationToken);

        return Responses.Status(HttpStatusCode.OK);
    }
}
