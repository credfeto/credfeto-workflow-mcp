using System.Collections.Generic;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IGitHubRestClient
{
    ValueTask<TResponse> GetAsync<TResponse>(
        string relativeUri,
        JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken cancellationToken
    );

    ValueTask<IReadOnlyList<TItem>> GetAllPagesAsync<TItem>(
        string relativeUri,
        JsonTypeInfo<TItem[]> pageTypeInfo,
        CancellationToken cancellationToken
    );
}
