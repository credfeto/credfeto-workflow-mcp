using System.Text.Json.Serialization;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.Data.Models.Json;

[JsonSerializable(typeof(BranchRequiredChecksResponse))]
[JsonSerializable(typeof(PingResponse))]
[JsonSerializable(typeof(PullRequestComments))]
[JsonSerializable(typeof(int))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
public sealed partial class AppJsonContext : JsonSerializerContext;
