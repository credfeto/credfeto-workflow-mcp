using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// Login is empty when the author's email is not linked to a GitHub account
[DebuggerDisplay("{Login} <{Email}>")]
public sealed record CommitAuthorIdentity(string Login, string Email, string Name);
