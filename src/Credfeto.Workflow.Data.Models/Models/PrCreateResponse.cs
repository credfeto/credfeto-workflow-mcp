using System;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Url}: Created={Created}")]
public sealed record PrCreateResponse(Uri Url, bool Created, string? Note);
