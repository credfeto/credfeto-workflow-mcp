using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Interfaces.Models;

namespace Credfeto.Workflow.GitHub.Interfaces;

public interface IGitHubGraphQlClient
{
    ValueTask<TData> ExecuteAsync<TVariables, TData>(
        string query,
        TVariables variables,
        JsonTypeInfo<TVariables> variablesTypeInfo,
        JsonTypeInfo<GraphQlResponse<TData>> responseTypeInfo,
        CancellationToken cancellationToken
    );
}
