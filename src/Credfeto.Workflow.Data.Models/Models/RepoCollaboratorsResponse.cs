using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Repository}: {Collaborators.Count}")]
public sealed record RepoCollaboratorsResponse(string Repository, IReadOnlyList<RepoCollaborator> Collaborators);
