using System.Collections.Generic;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{Oid}")]
public sealed record PullRequestCommitIdentity(string Oid, IReadOnlyList<CommitAuthorIdentity> Authors);
