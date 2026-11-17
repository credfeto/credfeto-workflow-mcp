using System;
using System.Threading;
using System.Threading.Tasks;

namespace Credfeto.Workflow.GitHub.Interfaces;

// Uses the server's board token, so it takes no status: a new item can only ever be added as Not Started
public interface IBoardFallbackWriter
{
    bool IsConfigured { get; }

    ValueTask CheckNotStartedAsync(string repository, CancellationToken cancellationToken);

    ValueTask AddAsNotStartedAsync(
        string repository,
        string contentNodeId,
        Uri url,
        CancellationToken cancellationToken
    );
}
