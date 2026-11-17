using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Configuration;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Microsoft.Extensions.Options;

namespace Credfeto.Workflow.GitHub.Authorization;

// Sends the server's board token, never the caller's, so it is only ever added to the board client
public sealed class BoardTokenHandler : DelegatingHandler
{
    private readonly IOptions<GitHubOptions> _options;

    public BoardTokenHandler(IOptions<GitHubOptions> options)
    {
        this._options = options;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        // An unauthenticated upstream call would silently use GitHub's anonymous rate limit and visibility
        string token = this._options.Value.Board.Token ?? throw new BoardTokenMissingException();

        request.Headers.Authorization = new AuthenticationHeaderValue(
            scheme: CallerBearerToken.Scheme,
            parameter: token
        );

        return base.SendAsync(request: request, cancellationToken: cancellationToken);
    }
}
