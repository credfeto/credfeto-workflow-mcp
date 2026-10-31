using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Board;

// The query aliases the issue or pull request accessor to item, so one shape serves both kinds
[DebuggerDisplay("Item={Item}")]
internal sealed record StatusQueryRepository([property: JsonPropertyName("item")] StatusQueryItem? Item);
