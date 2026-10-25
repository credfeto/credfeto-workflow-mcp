using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Credfeto.Workflow.GitHub.Tests.Helpers;

// A body that never arrives, like an upstream that sends the headers and then stalls
internal sealed class StallingContent : HttpContent
{
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        return this.SerializeToStreamAsync(stream: stream, context: context, cancellationToken: CancellationToken.None);
    }

    protected override Task SerializeToStreamAsync(
        Stream stream,
        TransportContext? context,
        CancellationToken cancellationToken
    )
    {
        return Task.Delay(delay: Timeout.InfiniteTimeSpan, cancellationToken: cancellationToken);
    }

    protected override bool TryComputeLength(out long length)
    {
        length = 0;

        return false;
    }
}
