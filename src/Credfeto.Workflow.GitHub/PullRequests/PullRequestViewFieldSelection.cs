using System;
using System.Collections.Generic;
using System.Linq;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Validation;

namespace Credfeto.Workflow.GitHub.PullRequests;

internal static class PullRequestViewFieldSelection
{
    // Runs before any network call so a bad request never costs an upstream round trip
    public static IReadOnlyList<string> Resolve(IReadOnlyList<string>? fields, string parameterName)
    {
        if (fields is null || fields.Count == 0)
        {
            return PullRequestViewFields.Default;
        }

        if (fields.Any(field => field is null || !PullRequestViewQuery.IsKnown(field)))
        {
            throw ParameterValidationException.For(
                parameterName: parameterName,
                rule: "must only contain known field names"
            );
        }

        HashSet<string> requested = new(collection: fields, comparer: StringComparer.Ordinal);

        if (
            requested.Count > 1
            && (requested.Contains(PullRequestViewFields.Files) || requested.Contains(PullRequestViewFields.Body))
        )
        {
            throw ParameterValidationException.For(
                parameterName: parameterName,
                rule: "must request files or body on its own"
            );
        }

        return [.. PullRequestViewFields.All.Where(requested.Contains)];
    }
}
