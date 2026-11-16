using System;
using System.Diagnostics;

namespace Credfeto.Workflow.GitHub.Configuration;

[DebuggerDisplay("ApiBaseUrl: {ApiBaseUrl}, ApiVersion: {ApiVersion}, UpstreamBasePath: {UpstreamBasePath}")]
public sealed class GitHubOptions
{
    public const string SectionName = "GitHub";

    public const string DefaultApiVersion = "2026-03-10";

    public Uri? ApiBaseUrl { get; set; }

    // An empty or whitespace value sends no version header, for GitHub Enterprise Server releases that reject it
    public string? ApiVersion { get; set; } = DefaultApiVersion;

    // The base path the GitHub server behind a proxy serves REST from, when it differs from the ApiBaseUrl path;
    // unset means the upstream is taken to serve REST from the same path as ApiBaseUrl
    public string? UpstreamBasePath { get; set; }
}
