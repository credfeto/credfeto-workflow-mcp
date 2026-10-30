using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Branches;

[DebuggerDisplay("{Context} from {AppId}")]
internal sealed record StatusCheck(
    [property: JsonPropertyName("context")] string? Context,
    [property: JsonPropertyName("app_id")] long? AppId
);
