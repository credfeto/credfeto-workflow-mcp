using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.GitHub.Interfaces.Models;

[DebuggerDisplay("FieldId={FieldId}, FieldName={FieldName}, Options={OptionIds.Count}")]
public sealed record WorkflowStatusField(
    string FieldId,
    string FieldName,
    IReadOnlyDictionary<string, string> OptionIds
);
