using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// PublishedAt is GitHub's ISO-8601 text; Name is "-" when the release has none
[DebuggerDisplay("{Tag}: {PublishedAt}, Latest={IsLatest}, Prerelease={IsPrerelease}")]
public sealed record ReleaseSummary(string Tag, string PublishedAt, bool IsLatest, bool IsPrerelease, string Name);
