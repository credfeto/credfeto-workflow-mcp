using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// The body is exactly as GitHub holds it: no line limit and no character replacement
[DebuggerDisplay("{Id}: {Author}")]
public sealed record IssueViewComment(string Id, string Author, string CreatedAt, string UpdatedAt, string Body);
