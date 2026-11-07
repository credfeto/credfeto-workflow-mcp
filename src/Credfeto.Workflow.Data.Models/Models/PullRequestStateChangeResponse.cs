using System;
using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("{PullRequest}: {Outcome}")]
public sealed record PullRequestStateChangeResponse(string PullRequest, string Outcome, Uri Url);
