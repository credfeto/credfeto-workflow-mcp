using System;
using System.Diagnostics;

namespace Credfeto.Workflow.GitHub.Configuration;

[DebuggerDisplay("ApiBaseUrl: {ApiBaseUrl}")]
public sealed class GitHubOptions
{
    public const string SectionName = "GitHub";

    public Uri? ApiBaseUrl { get; set; }
}
