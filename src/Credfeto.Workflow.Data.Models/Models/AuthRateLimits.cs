using System.Diagnostics;

namespace Credfeto.Workflow.Data.Models.Models;

[DebuggerDisplay("Core: {Core.Remaining}/{Core.Limit}, GraphQL: {Graphql.Remaining}/{Graphql.Limit}")]
public sealed record AuthRateLimits(AuthRateLimit Core, AuthRateLimit Graphql);
