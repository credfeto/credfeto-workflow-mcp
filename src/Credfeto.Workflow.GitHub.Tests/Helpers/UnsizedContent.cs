using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Credfeto.Workflow.GitHub.Tests.Helpers;

// A body sent without a Content-Length, like one the primary handler is still decompressing
internal sealed class UnsizedContent : HttpContent
{
    private readonly byte[] _body;

    public UnsizedContent(byte[] body)
    {
        this._body = body;
    }

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
        return stream.WriteAsync(buffer: this._body, cancellationToken: cancellationToken).AsTask();
    }

    protected override bool TryComputeLength(out long length)
    {
        length = 0;

        return false;
    }
}
