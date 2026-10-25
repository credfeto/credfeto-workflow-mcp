using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Credfeto.Workflow.GitHub.Interfaces.Exceptions;

[Description("The GitHub GraphQL request failed")]
public sealed partial class GitHubGraphQlException : Exception
{
    // Upstream error text is kept off Message, which callers can see, so code can choose what to show, log or map
    public IReadOnlyList<string> Errors { get; init; } = [];
}
