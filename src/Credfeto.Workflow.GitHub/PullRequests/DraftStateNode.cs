using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.PullRequests;

[DebuggerDisplay("IsDraft={IsDraft}")]
internal sealed record DraftStateNode([property: JsonPropertyName("isDraft")] bool? IsDraft);
