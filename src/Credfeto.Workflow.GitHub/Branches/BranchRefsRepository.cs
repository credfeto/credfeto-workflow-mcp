using System.Diagnostics;
using System.Text.Json.Serialization;
using Credfeto.Workflow.GitHub.Repositories;

namespace Credfeto.Workflow.GitHub.Branches;

// Each ref is null when that branch does not exist
[DebuggerDisplay("{Base} {Head}")]
internal sealed record BranchRefsRepository(
    [property: JsonPropertyName("base")] RepositoryListBranchRef? Base,
    [property: JsonPropertyName("head")] RepositoryListBranchRef? Head
);
