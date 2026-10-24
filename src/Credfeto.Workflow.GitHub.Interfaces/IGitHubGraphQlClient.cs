using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IGitHubGraphQlClient
{
    ValueTask<TData> ExecuteAsync<TVariables, TData>(
        string query,
        TVariables variables,
        JsonTypeInfo<TVariables> variablesTypeInfo,
        JsonTypeInfo<TData> dataTypeInfo,
        CancellationToken cancellationToken
    );
}
