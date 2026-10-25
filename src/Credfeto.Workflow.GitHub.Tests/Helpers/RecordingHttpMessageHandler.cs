using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Credfeto.Workflow.GitHub.Tests.Helpers;

internal sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<int, HttpResponseMessage> _respond;
    private readonly List<RecordedRequest> _requests = [];
    private readonly Lock _lock = new();
    private readonly TimeSpan _responseDelay;

    public RecordingHttpMessageHandler(Func<int, HttpResponseMessage> respond, TimeSpan responseDelay = default)
    {
        this._respond = respond;
        this._responseDelay = responseDelay;
    }

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (this._lock)
            {
                return [.. this._requests];
            }
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        RecordedRequest recorded = new(
            Method: request.Method,
            RequestUri: request.RequestUri,
            Authorization: request.Headers.Authorization?.ToString(),
            Body: body
        );

        int index;

        lock (this._lock)
        {
            this._requests.Add(recorded);
            index = this._requests.Count - 1;
        }

        // Recorded first, so an attempt cut short by a timeout is still counted
        await Task.Delay(delay: this._responseDelay, cancellationToken: cancellationToken);

        return this._respond(index);
    }
}
