using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;

namespace Credfeto.Workflow.GitHub.Advisories;

internal static class AdvisoryViewProjector
{
    // Only the values GitHub documents are passed on, so an unexpected value is never shown as if it were one
    private static readonly FrozenSet<string> Severities = FrozenSet.Create(
        equalityComparer: StringComparer.Ordinal,
        "critical",
        "high",
        "low",
        "medium",
        "unknown"
    );

    public static AdvisoryViewResult Project(string ghsa, GlobalAdvisoryResponse advisory)
    {
        // An answer for any other advisory is refused, so it is never shown as the one asked for
        if (!StringComparer.Ordinal.Equals(x: advisory.GhsaId, y: ghsa))
        {
            throw new GitHubInvalidResponseException(
                "GitHub returned a security advisory other than the one asked for"
            );
        }

        return new(
            GhsaId: ghsa,
            Severity: Severity(advisory.Severity),
            Summary: Text(advisory.Summary),
            Published: Text(advisory.PublishedAt),
            Withdrawn: Text(advisory.WithdrawnAt),
            Packages: Packages(advisory.Vulnerabilities)
        );
    }

    private static string Severity(string? severity)
    {
        return severity is not null && Severities.Contains(severity)
            ? severity
            : throw new GitHubInvalidResponseException("GitHub returned a security advisory with an unknown severity");
    }

    // An advisory not yet tied to any package has no vulnerabilities, which is shown as no affected packages
    private static IReadOnlyList<AdvisoryAffectedPackage> Packages(
        IReadOnlyList<GlobalAdvisoryVulnerabilityResponse?>? vulnerabilities
    )
    {
        return vulnerabilities is null ? [] : [.. vulnerabilities.Select(Package)];
    }

    private static AdvisoryAffectedPackage Package(GlobalAdvisoryVulnerabilityResponse? vulnerability)
    {
        return vulnerability is null
            ? throw new GitHubInvalidResponseException(
                "GitHub returned a security advisory with an empty vulnerability"
            )
            : new(
                Ecosystem: Text(vulnerability.Package?.Ecosystem),
                Package: Text(vulnerability.Package?.Name),
                VulnerableRange: Text(vulnerability.VulnerableVersionRange),
                FirstPatched: Text(vulnerability.FirstPatchedVersion)
            );
    }

    private static string Text(string? value)
    {
        return value is null ? string.Empty : UpstreamText.ReplaceLineBreaking(value);
    }
}
