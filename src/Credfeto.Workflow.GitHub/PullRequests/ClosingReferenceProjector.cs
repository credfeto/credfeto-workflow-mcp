using System;
using System.Collections.Generic;
using System.Linq;
using Credfeto.Workflow.Data.Models.Models;

namespace Credfeto.Workflow.GitHub.PullRequests;

// Shared by pr_list and pr_view so both report a pull request's closing issues the same way
internal static class ClosingReferenceProjector
{
    // One page only: a pull request closing more than 100 issues is refused rather than listed in part
    public const string SELECTION =
        "closingIssuesReferences(first:100){totalCount nodes{number repository{nameWithOwner}}}";

    // Returns null when the connection is missing, cut short or holds a node without a number or repository
    public static IReadOnlyList<ClosingIssueReference>? TryProject(GraphQlConnection<ClosingReferenceNode>? connection)
    {
        if (connection is not { TotalCount: int total, Nodes: { } nodes } || total != nodes.Count)
        {
            return null;
        }

        List<ClosingIssueReference> references = [];

        foreach (ClosingReferenceNode? node in nodes)
        {
            if (
                node is not { Number: int number, Repository.NameWithOwner: { } repository }
                || number <= 0
                || !IsRepository(repository)
            )
            {
                return null;
            }

            references.Add(new(Repository: repository, Number: number));
        }

        return references;
    }

    // Exactly one separator between a non-empty owner and name, using only the characters GitHub allows in either
    private static bool IsRepository(string value)
    {
        int separator = value.IndexOf('/', StringComparison.Ordinal);

        return separator > 0
            && separator < value.Length - 1
            && !value.AsSpan(start: separator + 1).Contains('/')
            && value.All(character =>
                character == '/' || char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'
            );
    }
}
