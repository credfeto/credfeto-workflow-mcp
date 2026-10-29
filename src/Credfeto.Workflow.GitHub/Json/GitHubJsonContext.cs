using System.Text.Json.Serialization;
using Credfeto.Workflow.GitHub.Board;
using Credfeto.Workflow.GitHub.Models;

namespace Credfeto.Workflow.GitHub.Json;

[JsonSerializable(typeof(GraphQlResponse))]
[JsonSerializable(typeof(ProjectsQueryVariables))]
[JsonSerializable(typeof(ProjectsQueryData))]
internal sealed partial class GitHubJsonContext : JsonSerializerContext;
