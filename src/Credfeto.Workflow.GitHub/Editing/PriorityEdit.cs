using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Issues;
using Credfeto.Workflow.GitHub.Json;
using Credfeto.Workflow.GitHub.Labels;

namespace Credfeto.Workflow.GitHub.Editing;

// An edit's priority replaces every priority label on the item, so the item ends with exactly one
internal static class PriorityEdit
{
    private const string PRIORITY_SUBJECT = "priority";

    private const string SECURITY = "Security";

    private const string SECURITY_NEVER_REPLACED_RULE = "Security is never replaced";

    private const string ALREADY_EXISTS = "already_exists";

    public static string? ValidPriority(string? priority)
    {
        if (priority is null)
        {
            return null;
        }

        return StandardLabels.FindPriority(priority)
            ?? throw ItemEdit.Usage(
                parameterName: nameof(priority),
                message: $"unknown priority (expected {StandardLabels.PriorityList})"
            );
    }

    // A Security issue must stay visible as one, so any other priority on it is refused
    public static EditRefusal? CheckPriority(EditRequest request, EditState state)
    {
        return request.Priority is { } priority && !IsSecurity(priority) && state.Labels.Any(IsSecurity)
            ? new EditRefusal(Subject: PRIORITY_SUBJECT, Rule: SECURITY_NEVER_REPLACED_RULE, LineNumber: null)
            : null;
    }

    public static PriorityChange Plan(EditRequest request, EditState state)
    {
        if (request.Priority is not { } priority)
        {
            return PriorityChange.None;
        }

        string[] current = [.. state.Labels.Where(StandardLabels.IsPriority)];

        if (current is [{ } only] && StringComparer.OrdinalIgnoreCase.Equals(x: only, y: priority))
        {
            return PriorityChange.None;
        }

        return new(
            Labels: [.. state.Labels.Where(static label => !StandardLabels.IsPriority(label)), priority],
            OldPriority: current.Length == 0 ? null : string.Join(separator: ", ", value: current),
            NewPriority: priority,
            LabelIsOnItem: current.Contains(value: priority, comparer: StringComparer.OrdinalIgnoreCase)
        );
    }

    // GitHub creates a label it does not know with no colour, so a missing priority label is created first with its
    // standard one. Returns why it could not be, or null when the label is there
    public static async ValueTask<string?> EnsureLabelExistsAsync(
        IGitHubRestClient restClient,
        EditRequest request,
        PriorityChange change,
        CancellationToken cancellationToken
    )
    {
        if (change.LabelIsOnItem || change.NewPriority is not { } label)
        {
            return null;
        }

        try
        {
            IReadOnlyList<IssueLabelResponse?> labels = await restClient.GetAllPagesAsync(
                relativeUri: $"repos/{request.Repository}/labels?per_page=100",
                pageTypeInfo: GitHubJsonContext.Default.IssueLabelResponseArray,
                cancellationToken: cancellationToken
            );

            if (labels.Any(static existing => existing?.Name is null))
            {
                return $"could not read the labels of {request.Repository}";
            }

            if (labels.Any(existing => StringComparer.OrdinalIgnoreCase.Equals(x: existing?.Name, y: label)))
            {
                return null;
            }
        }
        catch (Exception exception) when (ItemEdit.IsGitHubFailure(exception))
        {
            return $"could not read the labels of {request.Repository}";
        }

        return await CreateLabelAsync(
            restClient: restClient,
            repository: request.Repository,
            label: label,
            cancellationToken: cancellationToken
        );
    }

    private static async ValueTask<string?> CreateLabelAsync(
        IGitHubRestClient restClient,
        string repository,
        string label,
        CancellationToken cancellationToken
    )
    {
        StandardLabel? standard = StandardLabels.FindDefinition(label);

        try
        {
            await restClient.PostAsync(
                relativeUri: $"repos/{repository}/labels",
                body: new LabelCreateRequest(Name: label, Color: standard?.Colour, Description: standard?.Description),
                requestTypeInfo: GitHubJsonContext.Default.LabelCreateRequest,
                responseTypeInfo: GitHubJsonContext.Default.IssueLabelResponse,
                cancellationToken: cancellationToken
            );

            return null;
        }
        catch (HttpRequestException exception)
            when (GitHubHttpFailure.HasErrorCode(exception: exception, code: ALREADY_EXISTS))
        {
            // The label was created by someone else between the listing and this call, which is what was wanted
            return null;
        }
        catch (Exception exception) when (ItemEdit.IsGitHubFailure(exception))
        {
            // The label is one of the fixed priority names, so it is safe to name
            return $"could not create the label '{label}' in {repository}";
        }
    }

    private static bool IsSecurity(string label)
    {
        return StringComparer.OrdinalIgnoreCase.Equals(x: label, y: SECURITY);
    }
}
