using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Credfeto.Workflow.GitHub.Leaks;

public static partial class RepositoryReferenceScanner
{
    private const string OWNER = "(?<owner>[A-Za-z0-9][A-Za-z0-9-]*)";
    private const string NAME = "(?<name>[A-Za-z0-9_.-]+)";

    // A host preceded by a single '.' is a different host (docs.github.com, gist.github.com), but an ellipsis is prose
    private const string LINK =
        @"(?<=^|[^A-Za-z0-9.-]|\.\.)\.*(?i:(www\.)?github\.com[:/]|api\.github\.com/repos/|raw\.githubusercontent\.com/|codeload\.github\.com/)"
        + OWNER
        + "/"
        + NAME;

    // A path segment, URL fragment or action version (owner/action@v4) is not a cross-reference, so the reference must
    // stand alone and end in an issue number or a commit SHA; '_' may wrap it as Markdown emphasis
    private const string CROSS_REFERENCE =
        "(?<=^|[^A-Za-z0-9_./~@#%?&=+-])_*"
        + OWNER
        + "/"
        + NAME
        + "(#[0-9]+|@[0-9a-fA-F]{7,40})(?=_*($|[^A-Za-z0-9_-]))";

    private const string BARE = "(?<=^|[^A-Za-z0-9_./-])" + OWNER + "/" + NAME;

    // The first path segments of github.com that are not owners
    private static readonly FrozenSet<string> ReservedOwners = FrozenSet.Create(
        equalityComparer: StringComparer.OrdinalIgnoreCase,
        "about",
        "account",
        "advisories",
        "apps",
        "codespaces",
        "collections",
        "contact",
        "copilot",
        "customer-stories",
        "dashboard",
        "enterprise",
        "enterprises",
        "events",
        "explore",
        "features",
        "issues",
        "join",
        "login",
        "logout",
        "marketplace",
        "new",
        "notifications",
        "organizations",
        "orgs",
        "pricing",
        "pulls",
        "readme",
        "resources",
        "search",
        "security",
        "sessions",
        "settings",
        "signup",
        "site",
        "solutions",
        "sponsors",
        "stars",
        "team",
        "topics",
        "trending",
        "user-attachments",
        "users",
        "watching"
    );

    public static IReadOnlyList<RepositoryReference> Scan(string text, FrozenSet<string> privateOwners)
    {
        string[] lines = text.Split('\n');
        List<RepositoryReference> references = [];

        for (int index = 0; index < lines.Length; ++index)
        {
            references.AddRange(ScanLine(line: lines[index], lineNumber: index + 1, privateOwners: privateOwners));
        }

        return references;
    }

    private static IEnumerable<RepositoryReference> ScanLine(
        string line,
        int lineNumber,
        FrozenSet<string> privateOwners
    )
    {
        IEnumerable<(int Position, RepositoryReference Reference)> links = LeakPatterns
            .Matches(pattern: Link(), line: line)
            .Where(match => !ReservedOwners.Contains(match.Groups["owner"].Value))
            .Select(match => ToReference(match: match, lineNumber: lineNumber));

        IEnumerable<(int Position, RepositoryReference Reference)> crossReferences = LeakPatterns
            .Matches(pattern: CrossReference(), line: line)
            .Where(match => !IsPort(match: match, line: line))
            .Select(match => ToReference(match: match, lineNumber: lineNumber));

        IEnumerable<(int Position, RepositoryReference Reference)> bare = LeakPatterns
            .Matches(pattern: Bare(), line: line)
            .Where(match => privateOwners.Contains(match.Groups["owner"].Value))
            .Select(match => ToReference(match: match, lineNumber: lineNumber));

        return
        [
            .. links
                .Concat(crossReferences)
                .Concat(bare)
                .Where(candidate => candidate.Reference.Name.Length != 0)
                .OrderBy(candidate => candidate.Position)
                .Select(candidate => candidate.Reference),
        ];
    }

    // host:8080/path#1 is a port and a path rather than a cross-reference
    private static bool IsPort(Match match, string line)
    {
        Group owner = match.Groups["owner"];

        return owner.Index > 0 && line[owner.Index - 1] == ':' && owner.Value.All(char.IsAsciiDigit);
    }

    private static (int Position, RepositoryReference Reference) ToReference(Match match, int lineNumber)
    {
        Group owner = match.Groups["owner"];

        return (
            owner.Index,
            new(LineNumber: lineNumber, Owner: owner.Value, Name: CleanName(match.Groups["name"].Value))
        );
    }

    private static string CleanName(string name)
    {
        string trimmed = name.TrimEnd('.');

        trimmed = RemoveSuffix(value: trimmed, suffix: ".git");

        return RemoveSuffix(value: trimmed, suffix: ".wiki");
    }

    private static string RemoveSuffix(string value, string suffix)
    {
        return value.EndsWith(value: suffix, comparisonType: StringComparison.OrdinalIgnoreCase)
            ? value[..^suffix.Length]
            : value;
    }

    [GeneratedRegex(
        pattern: LINK,
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: LeakPatterns.REGEX_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex Link();

    [GeneratedRegex(
        pattern: CROSS_REFERENCE,
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: LeakPatterns.REGEX_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex CrossReference();

    [GeneratedRegex(
        pattern: BARE,
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: LeakPatterns.REGEX_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex Bare();
}
