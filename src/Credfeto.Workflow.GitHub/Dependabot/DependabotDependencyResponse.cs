using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Dependabot;

[DebuggerDisplay("{ManifestPath}")]
internal sealed record DependabotDependencyResponse(
    [property: JsonPropertyName("package")] DependabotPackageResponse? Package,
    [property: JsonPropertyName("manifest_path")] string? ManifestPath
);
