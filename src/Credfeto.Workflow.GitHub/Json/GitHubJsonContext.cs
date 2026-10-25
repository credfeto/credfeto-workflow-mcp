using System.Text.Json.Serialization;
using Credfeto.Workflow.GitHub.Models;

namespace Credfeto.Workflow.GitHub.Json;

[JsonSerializable(typeof(GraphQlResponse))]
internal sealed partial class GitHubJsonContext : JsonSerializerContext;
