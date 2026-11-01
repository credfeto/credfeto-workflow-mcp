using System.Text.Json.Serialization;
using Credfeto.Workflow.GitHub.Board;
using Credfeto.Workflow.GitHub.Branches;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Leaks;
using Credfeto.Workflow.GitHub.Models;
using Credfeto.Workflow.GitHub.PullRequests;
using Credfeto.Workflow.GitHub.Status;

namespace Credfeto.Workflow.GitHub.Json;

[JsonSerializable(typeof(BranchResponse))]
[JsonSerializable(typeof(BranchRuleResponse[]))]
[JsonSerializable(typeof(RepositoryResponse))]
[JsonSerializable(typeof(GitHubErrorResponse))]
[JsonSerializable(typeof(GraphQlResponse))]
[JsonSerializable(typeof(ProjectsQueryVariables))]
[JsonSerializable(typeof(ProjectsQueryData))]
[JsonSerializable(typeof(PullRequestCommentsQueryVariables))]
[JsonSerializable(typeof(PullRequestCommentsQueryData))]
[JsonSerializable(typeof(StatusQueryVariables))]
[JsonSerializable(typeof(StatusQueryData))]
[JsonSerializable(typeof(BoardItemsQueryVariables))]
[JsonSerializable(typeof(BoardItemsQueryData))]
[JsonSerializable(typeof(NodeIdQueryData))]
[JsonSerializable(typeof(AddItemMutationVariables))]
[JsonSerializable(typeof(AddItemMutationData))]
[JsonSerializable(typeof(UpdateItemMutationVariables))]
[JsonSerializable(typeof(UpdateItemMutationData))]
[JsonSerializable(typeof(FieldsQueryVariables))]
[JsonSerializable(typeof(FieldsQueryData))]
[JsonSerializable(typeof(VisibilityQueryVariables))]
[JsonSerializable(typeof(VisibilityQueryData))]
internal sealed partial class GitHubJsonContext : JsonSerializerContext;
