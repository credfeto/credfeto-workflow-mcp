using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Tags;

// Every member is nullable because GitHub's answer is checked rather than trusted
[DebuggerDisplay("{Object}")]
internal sealed record GitTagResponse([property: JsonPropertyName("object")] GitObjectResponse? Object);
