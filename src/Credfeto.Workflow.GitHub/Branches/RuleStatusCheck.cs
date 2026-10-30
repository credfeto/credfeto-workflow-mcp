using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Branches;

[DebuggerDisplay("{Context} from {IntegrationId}")]
internal sealed record RuleStatusCheck(
    [property: JsonPropertyName("context")] string? Context,
    [property: JsonPropertyName("integration_id")] long? IntegrationId
);
