using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Labels;

[DebuggerDisplay("Labels={Labels.Count}")]
internal sealed record LabelAddRequest([property: JsonPropertyName("labels")] IReadOnlyList<string> Labels);
