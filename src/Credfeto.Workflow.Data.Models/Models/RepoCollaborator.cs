using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// Role is GitHub's role name, such as admin or a custom role, and "-" when GitHub gives none
[DebuggerDisplay("{Login}: {Role}")]
public sealed record RepoCollaborator(string Login, string Role);
