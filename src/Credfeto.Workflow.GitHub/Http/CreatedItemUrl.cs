using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Credfeto.Workflow.GitHub.Http;

internal static class CreatedItemUrl
{
    public const string NUMBER_GROUP = "number";

    // Neither the host nor the repository in the URL is compared with anything: GitHub may be reached through a proxy
    // that answers with its own host, and a renamed or transferred repository answers with its new name
    public static bool TryParse(Regex pattern, string url, [NotNullWhen(true)] out Uri? uri, out int number)
    {
        Match match = pattern.Match(url);

        if (
            match.Success
            && int.TryParse(
                s: match.Groups[NUMBER_GROUP].ValueSpan,
                style: NumberStyles.None,
                provider: CultureInfo.InvariantCulture,
                result: out number
            )
            && number > 0
            && Uri.TryCreate(uriString: url, uriKind: UriKind.Absolute, result: out uri)
        )
        {
            return true;
        }

        uri = null;
        number = 0;

        return false;
    }
}
