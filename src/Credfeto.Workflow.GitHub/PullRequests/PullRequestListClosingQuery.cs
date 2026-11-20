using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Credfeto.Workflow.GitHub.PullRequests;

internal static class PullRequestListClosingQuery
{
    // 50 pull requests of up to 100 references each stays far below GitHub's node limit for one query
    public const int BATCH_SIZE = 50;

    // Built only from constant text and validated pull request numbers, so no caller text reaches the query
    public static string Build(IEnumerable<int> numbers)
    {
        string selection = string.Join(
            separator: ' ',
            values: numbers.Select(number =>
                Alias(number)
                + ":pullRequest(number:"
                + Number(number)
                + "){"
                + ClosingReferenceProjector.SELECTION
                + "}"
            )
        );

        return "query($o:String!,$r:String!){repository(owner:$o,name:$r){" + selection + "}}";
    }

    public static string Alias(int number)
    {
        return "p" + Number(number);
    }

    private static string Number(int number)
    {
        return number.ToString(CultureInfo.InvariantCulture);
    }
}
