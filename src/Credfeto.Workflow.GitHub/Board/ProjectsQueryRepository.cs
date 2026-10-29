using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Board;

[DebuggerDisplay("ProjectsV2={ProjectsV2}")]
internal sealed record ProjectsQueryRepository(
    [property: JsonPropertyName("projectsV2")] ProjectsConnection? ProjectsV2
);
