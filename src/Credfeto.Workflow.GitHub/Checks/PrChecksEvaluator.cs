using System;
using System.Collections.Generic;
using System.Linq;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;

namespace Credfeto.Workflow.GitHub.Checks;

internal static class PrChecksEvaluator
{
    private const string CONCLUSION_SUCCESS = "success";
    private const string CONCLUSION_SKIPPED = "skipped";
    private const string CONCLUSION_NEUTRAL = "neutral";
    private const string CONCLUSION_CANCELLED = "cancelled";

    private const string STATE_SUCCESS = "success";
    private const string STATE_PENDING = "pending";

    public static PrChecksResponse Evaluate(
        string repository,
        string sha,
        IReadOnlyList<RequiredCheck> required,
        IReadOnlyList<CheckRunResponse?> runs,
        IReadOnlyList<CommitStatusResponse?> statuses,
        bool all
    )
    {
        IReadOnlyList<PrCheckEntry> entries = [.. runs.Select(ToEntry).Concat(statuses.Select(ToEntry))];
        IReadOnlyList<PrCheckEntry> latest = Latest(entries);

        IReadOnlyList<PrCheckResult> checks =
        [
            .. Requirements(required: required, latest: latest, all: all)
                .Select(requirement => Judge(requirement: requirement, latest: latest))
                .OrderBy(keySelector: check => check.Name, comparer: StringComparer.Ordinal),
        ];

        return new(
            Repo: repository,
            Sha: sha,
            Verdict: Overall(hasEntries: entries.Count != 0, checks: checks),
            Passed: Count(checks: checks, verdict: PrChecksVerdict.Passed),
            Failed: Count(checks: checks, verdict: PrChecksVerdict.Failed),
            Pending: Count(checks: checks, verdict: PrChecksVerdict.Pending),
            Checks: checks
        );
    }

    // The first required check, by name, that nothing reported in any state, finished or not
    public static RequiredCheck? FirstNeverRan(
        IReadOnlyList<RequiredCheck> required,
        IReadOnlyList<CheckRunResponse?> runs,
        IReadOnlyList<CommitStatusResponse?> statuses
    )
    {
        IReadOnlyList<PrCheckEntry> entries = [.. runs.Select(ToEntry).Concat(statuses.Select(ToEntry))];

        return required
            .Where(requirement => !entries.Any(entry => Reports(entry: entry, requirement: requirement)))
            .OrderBy(keySelector: requirement => requirement.Name, comparer: StringComparer.Ordinal)
            .ThenBy(requirement => requirement.AppId)
            .FirstOrDefault();
    }

    // A check pinned to an app is only reported by that app's runs, but a status of the same name reports it in any state
    private static bool Reports(PrCheckEntry entry, RequiredCheck requirement)
    {
        return StringComparer.Ordinal.Equals(x: entry.Name, y: requirement.Name)
            && (entry.IsStatus || requirement.AppId is null || entry.AppId == requirement.AppId);
    }

    private static PrCheckEntry ToEntry(CheckRunResponse? run)
    {
        if (run is null)
        {
            throw new GitHubInvalidResponseException("GitHub returned an empty check run");
        }

        return new(
            Name: ValidName(name: run.Name, kind: "check run"),
            AppId: run.App?.Id,
            IsStatus: false,
            Id: ValidId(id: run.Id, kind: "check run"),
            Conclusion: run.Conclusion,
            Verdict: RunVerdict(run.Conclusion),
            CompletedAt: run.CompletedAt
        );
    }

    private static PrCheckEntry ToEntry(CommitStatusResponse? status)
    {
        if (status is null)
        {
            throw new GitHubInvalidResponseException("GitHub returned an empty commit status");
        }

        return new(
            Name: ValidName(name: status.Context, kind: "commit status"),
            AppId: null,
            IsStatus: true,
            Id: ValidId(id: status.Id, kind: "commit status"),
            Conclusion: null,
            Verdict: StatusVerdict(status.State),
            CompletedAt: status.UpdatedAt
        );
    }

    private static string ValidName(string? name, string kind)
    {
        return string.IsNullOrEmpty(name)
            ? throw new GitHubInvalidResponseException($"GitHub returned a {kind} without a name")
            : name;
    }

    private static long ValidId(long? id, string kind)
    {
        return id ?? throw new GitHubInvalidResponseException($"GitHub returned a {kind} without an id");
    }

    private static PrChecksVerdict RunVerdict(string? conclusion)
    {
        return conclusion switch
        {
            null or "" => PrChecksVerdict.Pending,
            CONCLUSION_SUCCESS or CONCLUSION_SKIPPED or CONCLUSION_NEUTRAL => PrChecksVerdict.Passed,
            _ => PrChecksVerdict.Failed,
        };
    }

    private static PrChecksVerdict StatusVerdict(string? state)
    {
        return state switch
        {
            STATE_SUCCESS => PrChecksVerdict.Passed,
            STATE_PENDING => PrChecksVerdict.Pending,
            _ => PrChecksVerdict.Failed,
        };
    }

    // Only the newest report from each source counts, so a re-run replaces the run it repeats
    private static IReadOnlyList<PrCheckEntry> Latest(IReadOnlyList<PrCheckEntry> entries)
    {
        return
        [
            .. entries
                .GroupBy(entry => (entry.Name, entry.AppId, entry.IsStatus))
                .Select(group => group.OrderBy(entry => entry.Id).Aggregate(Newer)),
        ];
    }

    // A skipped run must not hide the failure it follows, but may stand in for a run that was skipped or cancelled
    private static PrCheckEntry Newer(PrCheckEntry current, PrCheckEntry next)
    {
        return
            IsConclusion(entry: next, conclusion: CONCLUSION_SKIPPED)
            && !IsConclusion(entry: current, conclusion: CONCLUSION_SKIPPED)
            && !IsConclusion(entry: current, conclusion: CONCLUSION_CANCELLED)
            ? current
            : next;
    }

    private static bool IsConclusion(PrCheckEntry entry, string conclusion)
    {
        return StringComparer.Ordinal.Equals(x: entry.Conclusion, y: conclusion);
    }

    private static IEnumerable<RequiredCheck> Requirements(
        IReadOnlyList<RequiredCheck> required,
        IReadOnlyList<PrCheckEntry> latest,
        bool all
    )
    {
        IEnumerable<string> seen = latest.Select(entry => entry.Name).Distinct(StringComparer.Ordinal);

        if (all)
        {
            HashSet<string> requiredNames = new(
                collection: required.Select(check => check.Name),
                comparer: StringComparer.Ordinal
            );

            return required.Concat(seen.Where(name => !requiredNames.Contains(name)).Select(ByName));
        }

        // With nothing required every check that reported is judged, so an unprotected branch still gets a verdict
        return required.Count != 0 ? required : seen.Select(ByName);
    }

    private static RequiredCheck ByName(string name)
    {
        return new(Name: name, AppId: null);
    }

    private static PrCheckResult Judge(RequiredCheck requirement, IReadOnlyList<PrCheckEntry> latest)
    {
        string baseName = UpstreamText.ReplaceLineBreaking(requirement.Name);
        string name = requirement.AppId is { } appId ? $"{baseName} (app {appId})" : baseName;

        PrCheckEntry? worst = latest
            .Where(entry => Satisfies(entry: entry, requirement: requirement))
            .OrderByDescending(entry => Severity(entry.Verdict))
            .ThenByDescending(entry => entry.Id)
            .FirstOrDefault();

        return worst is null || worst.Verdict == PrChecksVerdict.Pending
            ? new(Name: name, Verdict: PrChecksVerdict.Pending, CompletedAt: null, RunId: null)
            : new(Name: name, Verdict: worst.Verdict, CompletedAt: worst.CompletedAt, RunId: worst.Id);
    }

    // A check pinned to an app is only met by that app's runs, but a failing status of the same name still blocks it
    private static bool Satisfies(PrCheckEntry entry, RequiredCheck requirement)
    {
        if (!StringComparer.Ordinal.Equals(x: entry.Name, y: requirement.Name))
        {
            return false;
        }

        if (requirement.AppId is null)
        {
            return true;
        }

        return entry.IsStatus ? entry.Verdict == PrChecksVerdict.Failed : entry.AppId == requirement.AppId;
    }

    private static int Severity(PrChecksVerdict verdict)
    {
        return verdict switch
        {
            PrChecksVerdict.Failed => 2,
            PrChecksVerdict.Pending => 1,
            _ => 0,
        };
    }

    private static PrChecksVerdict Overall(bool hasEntries, IReadOnlyList<PrCheckResult> checks)
    {
        if (!hasEntries)
        {
            return PrChecksVerdict.NoChecks;
        }

        if (checks.Any(check => check.Verdict == PrChecksVerdict.Failed))
        {
            return PrChecksVerdict.Failed;
        }

        return checks.Any(check => check.Verdict == PrChecksVerdict.Pending)
            ? PrChecksVerdict.Pending
            : PrChecksVerdict.Passed;
    }

    private static int Count(IReadOnlyList<PrCheckResult> checks, PrChecksVerdict verdict)
    {
        return checks.Count(check => check.Verdict == verdict);
    }
}
