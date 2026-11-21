using System;
using System.Collections.Frozen;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Runs;

namespace Credfeto.Workflow.GitHub.Advisories;

internal static class SecurityAdvisoriesProjector
{
    private const string NO_SEVERITY = "-";

    // Only the values GitHub documents are passed on, so an unexpected value is never shown as if it were one
    private static readonly FrozenSet<string> States = FrozenSet.Create(
        equalityComparer: StringComparer.Ordinal,
        "closed",
        "draft",
        "published",
        "triage",
        "withdrawn"
    );

    private static readonly FrozenSet<string> Severities = FrozenSet.Create(
        equalityComparer: StringComparer.Ordinal,
        "critical",
        "high",
        "low",
        "medium"
    );

    public static SecurityAdvisoryEntry Project(SecurityAdvisoryResponse? advisory)
    {
        // The id is not echoed in the error, since it failed validation and could carry anything
        if (advisory?.GhsaId is not { } ghsaId || !GitHubNames.IsGhsaId(ghsaId))
        {
            throw new GitHubInvalidResponseException("GitHub returned a security advisory without a valid GHSA id");
        }

        return new(
            GhsaId: ghsaId,
            Severity: Severity(advisory.Severity),
            State: Known(value: advisory.State, known: States, field: "state"),
            Summary: RunText.Field(advisory.Summary)
        );
    }

    // A draft advisory may have no severity yet, which is shown as a dash rather than refused
    private static string Severity(string? severity)
    {
        return string.IsNullOrEmpty(severity)
            ? NO_SEVERITY
            : Known(value: severity, known: Severities, field: "severity");
    }

    private static string Known(string? value, FrozenSet<string> known, string field)
    {
        return value is not null && known.Contains(value)
            ? value
            : throw new GitHubInvalidResponseException($"GitHub returned a security advisory with an unknown {field}");
    }
}
