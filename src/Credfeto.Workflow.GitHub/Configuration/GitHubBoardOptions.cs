using System.Diagnostics;

namespace Credfeto.Workflow.GitHub.Configuration;

// A class rather than a record, so no generated ToString can ever print the token
[DebuggerDisplay("IsConfigured: {IsConfigured}")]
public sealed class GitHubBoardOptions
{
    // Used only to put a newly created issue on the Workflow board as Not Started when the caller's token cannot
    public string? Token { get; set; }

    public bool IsConfigured => this.Token is not null;
}
