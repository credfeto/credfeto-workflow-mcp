using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Dependabot;

[DebuggerDisplay("{Identifier}")]
internal sealed record DependabotPatchedVersionResponse([property: JsonPropertyName("identifier")] string? Identifier);
