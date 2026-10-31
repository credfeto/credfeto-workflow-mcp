using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Board;

// GitHub returns a null node, with an error, when the project does not exist or the token cannot see it
[DebuggerDisplay("Node={Node}")]
internal sealed record BoardItemsQueryData([property: JsonPropertyName("node")] BoardItemsProject? Node);
