using System;
using System.Collections.Generic;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Microsoft.Extensions.Options;

namespace Credfeto.Workflow.GitHub.Configuration;

public sealed class WorkflowOptionsValidator : IValidateOptions<WorkflowOptions>
{
    public ValidateOptionsResult Validate(string? name, WorkflowOptions options)
    {
        try
        {
            _ = ParsePrivateOwners(options.PrivateOwners);

            return ValidateOptionsResult.Success;
        }
        catch (ParameterValidationException)
        {
            return ValidateOptionsResult.Fail(
                "Workflow:PrivateOwners must be a space-separated list of GitHub owner names; each may only contain letters, digits and '-' and must not start with '-'."
            );
        }
    }

    public static IReadOnlyList<string> ParsePrivateOwners(string? privateOwners)
    {
        if (string.IsNullOrWhiteSpace(privateOwners))
        {
            return [];
        }

        string[] owners = privateOwners.Split(separator: ' ', options: StringSplitOptions.RemoveEmptyEntries);

        foreach (string owner in owners)
        {
            _ = GitHubNames.Owner(value: owner, parameterName: nameof(WorkflowOptions.PrivateOwners));
        }

        return owners;
    }
}
