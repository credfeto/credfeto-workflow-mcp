using System.Text.RegularExpressions;

namespace Credfeto.Workflow.GitHub.BodyRules;

public static partial class ApprovalKeywordMatcher
{
    private const int REGEX_TIMEOUT_MILLISECONDS = 1000;

    public static bool HasKeyword(string body)
    {
        return FindFirstKeywordLine(body) is not null;
    }

    public static ApprovalKeywordMatch? FindFirstKeywordLine(string body)
    {
        // Split on \n first so ^ and $ mean line start and end, matching cfwf's line-by-line scan
        string[] lines = body.Split('\n');

        for (int index = 0; index < lines.Length; ++index)
        {
            string line = lines[index];

            // Quoting a human's own words is allowed, so quote lines are skipped while keeping line numbers
            if (QuoteLine().IsMatch(line))
            {
                continue;
            }

            if (Keyword().IsMatch(line))
            {
                return new ApprovalKeywordMatch(index + 1);
            }
        }

        return null;
    }

    // A deeper indent or a tab before > is Markdown code rather than a quote, so only up to three spaces qualify
    [GeneratedRegex(
        pattern: "^ {0,3}>",
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: REGEX_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex QuoteLine();

    // Underscores with only a word boundary beyond them are Markdown emphasis, which renders as the bare keyword,
    // whereas a letter, digit or underscore beyond them makes it part of a longer word
    [GeneratedRegex(
        pattern: "(^|[^A-Za-z0-9_])_*(approved|lgtm)_*([^A-Za-z0-9_]|$)",
        options: RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: REGEX_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex Keyword();
}
