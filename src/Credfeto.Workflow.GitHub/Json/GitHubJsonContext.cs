using System.Text.Json.Serialization;
using Credfeto.Workflow.GitHub.Board;
using Credfeto.Workflow.GitHub.Models;
using Credfeto.Workflow.GitHub.PullRequests;

namespace Credfeto.Workflow.GitHub.Json;

[JsonSerializable(typeof(GraphQlResponse))]
[JsonSerializable(typeof(ProjectsQueryVariables))]
[JsonSerializable(typeof(ProjectsQueryData))]
[JsonSerializable(typeof(PullRequestCommentsQueryVariables))]
[JsonSerializable(typeof(PullRequestCommentsQueryData))]
internal sealed partial class GitHubJsonContext : JsonSerializerContext;
