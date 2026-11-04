using System.Text.Json.Serialization;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.Data.Models.Json;

[JsonSerializable(typeof(BodyCheckResult))]
[JsonSerializable(typeof(BranchRequiredChecksResponse))]
[JsonSerializable(typeof(IssueCloseResponse))]
[JsonSerializable(typeof(IssueComments))]
[JsonSerializable(typeof(IssueCreateResponse))]
[JsonSerializable(typeof(IssueListResponse))]
[JsonSerializable(typeof(IssueSearchResult))]
[JsonSerializable(typeof(IssueViewResponse))]
[JsonSerializable(typeof(LabelChangeResponse))]
[JsonSerializable(typeof(PingResponse))]
[JsonSerializable(typeof(PrChecksResponse))]
[JsonSerializable(typeof(PrCreateResponse))]
[JsonSerializable(typeof(PrReplyResult))]
[JsonSerializable(typeof(PullRequestComments))]
[JsonSerializable(typeof(PullRequestListResponse))]
[JsonSerializable(typeof(PullRequestView))]
[JsonSerializable(typeof(WorkflowStatusCheckResult))]
[JsonSerializable(typeof(WorkflowStatusSetResponse))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(int?))]
[JsonSerializable(typeof(bool))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
public sealed partial class AppJsonContext : JsonSerializerContext;
