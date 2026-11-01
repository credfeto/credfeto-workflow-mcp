using System.Diagnostics;

namespace Credfeto.Workflow.GitHub.Interfaces.Models;

[DebuggerDisplay("FieldId={FieldId}, FieldName={FieldName}, OptionId={OptionId}")]
public sealed record ResolvedStatusOption(string FieldId, string FieldName, string OptionId);
