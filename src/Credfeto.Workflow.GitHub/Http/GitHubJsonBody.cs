using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;

namespace Credfeto.Workflow.GitHub.Http;

internal static class GitHubJsonBody
{
    // The fixed message is what callers see; the JsonException, whose message can quote the upstream body, is
    // kept only as the inner exception for server-side diagnostics. The raw stream is read rather than using
    // ReadFromJsonAsync, which honours the Content-Type charset and throws InvalidOperationException (escaping
    // this wrapping) when the upstream sends an unknown one
    public static async ValueTask<TContent> ReadAsync<TContent>(
        HttpContent content,
        JsonTypeInfo<TContent> typeInfo,
        string nullMessage,
        string unreadableMessage,
        CancellationToken cancellationToken
    )
    {
        await using Stream stream = await content.ReadAsStreamAsync(cancellationToken);

        try
        {
            return await JsonSerializer.DeserializeAsync(
                    utf8Json: stream,
                    jsonTypeInfo: typeInfo,
                    cancellationToken: cancellationToken
                ) ?? throw new GitHubInvalidResponseException(nullMessage);
        }
        catch (JsonException exception)
        {
            throw new GitHubInvalidResponseException(message: unreadableMessage, innerException: exception);
        }
    }
}
