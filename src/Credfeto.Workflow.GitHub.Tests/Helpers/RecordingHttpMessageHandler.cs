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

    public RecordingHttpMessageHandler(Func<int, HttpResponseMessage> respond)
    {
        this._respond = respond;
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

        lock (this._lock)
        {
            this._requests.Add(recorded);

            return this._respond(this._requests.Count - 1);
        }
    }
}
