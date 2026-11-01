using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Board;

// The query aliases the issue or pull request accessor to item, so one shape serves both kinds; GitHub returns a
// null item, with an error, when the work item does not exist
[DebuggerDisplay("Item={Item}")]
internal sealed record NodeIdQueryRepository([property: JsonPropertyName("item")] GraphQlNode? Item);
