using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Credfeto.Workflow.GitHub.Board;

internal static partial class BoardMutations
{
    public const string ADD_ITEM =
        "mutation($p:ID!,$c:ID!){addProjectV2ItemById(input:{projectId:$p,contentId:$c}){item{id}}}";

    public const string UPDATE_ITEM =
        "mutation($p:ID!,$i:ID!,$f:ID!,$o:String!){updateProjectV2ItemFieldValue(input:{projectId:$p,itemId:$i,fieldId:$f,value:{singleSelectOptionId:$o}}){projectV2Item{id}}}";

    private const int REGEX_TIMEOUT_MILLISECONDS = 1000;

    public static bool IsNodeId([NotNullWhen(true)] string? nodeId)
    {
        return nodeId is not null && NodeId().IsMatch(nodeId);
    }

    // \z rather than $ because in .NET $ also matches before a trailing newline, which would let one through
    [GeneratedRegex(
        pattern: @"^[A-Za-z0-9_=-]+\z",
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: REGEX_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex NodeId();
}
