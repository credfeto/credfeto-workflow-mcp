using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// Sha is always the commit the tag points to, never an annotated tag's own object
[DebuggerDisplay("{Sha}: {Type}")]
public sealed record TagViewResponse(string Sha, TagType Type);
