using System;
using System.Collections.Frozen;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.PullRequests;
using Credfeto.Workflow.GitHub.Runs;

namespace Credfeto.Workflow.GitHub.Dependabot;

internal static class DependabotAlertsProjector
{
    // Only the values GitHub documents are passed on, so an unexpected value is never shown as if it were one
    private static readonly FrozenSet<string> States = FrozenSet.Create(
        equalityComparer: StringComparer.Ordinal,
        "auto_dismissed",
        "dismissed",
        "fixed",
        "open"
    );

    private static readonly FrozenSet<string> Severities = FrozenSet.Create(
        equalityComparer: StringComparer.Ordinal,
        "critical",
        "high",
        "low",
        "medium"
    );

    public static DependabotAlertEntry Project(DependabotAlertResponse? alert)
    {
        if (alert?.Number is not ({ } number and > 0))
        {
            throw new GitHubInvalidResponseException("GitHub returned a Dependabot alert without a number");
        }

        string package = PullRequestViewText.Clean(alert.Dependency?.Package?.Name);

        if (string.IsNullOrWhiteSpace(package))
        {
            throw new GitHubInvalidResponseException("GitHub returned a Dependabot alert without a package");
        }

        return new(
            Number: number,
            Severity: Known(value: alert.SecurityAdvisory?.Severity, known: Severities, field: "severity"),
            State: Known(value: alert.State, known: States, field: "state"),
            Ecosystem: RunText.Field(alert.Dependency?.Package?.Ecosystem),
            Package: package,
            ManifestPath: RunText.Field(alert.Dependency?.ManifestPath),
            VulnerableRange: RunText.Field(alert.SecurityVulnerability?.VulnerableVersionRange),
            FirstPatchedVersion: RunText.Field(alert.SecurityVulnerability?.FirstPatchedVersion?.Identifier),
            GhsaId: RunText.Field(alert.SecurityAdvisory?.GhsaId)
        );
    }

    private static string Known(string? value, FrozenSet<string> known, string field)
    {
        return value is not null && known.Contains(value)
            ? value
            : throw new GitHubInvalidResponseException($"GitHub returned a Dependabot alert with an unknown {field}");
    }
}
