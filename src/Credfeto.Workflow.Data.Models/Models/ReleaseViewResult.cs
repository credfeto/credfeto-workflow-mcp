using System;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

// PublishedAt is GitHub's ISO-8601 text, as cfwf prints it; Name is empty when the release has none
[DebuggerDisplay("{Tag}: {PublishedAt}")]
public sealed record ReleaseViewResult(string Tag, string PublishedAt, Uri Url, string Name);
