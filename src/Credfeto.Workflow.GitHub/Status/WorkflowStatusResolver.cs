using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Json;
using Credfeto.Workflow.GitHub.Models;

namespace Credfeto.Workflow.GitHub.Status;

public sealed class WorkflowStatusResolver : IWorkflowStatusResolver
{
    private const string STATUS_FIELD_NAME = "Status";

    // Boards set up before the built-in Status field was renamed keep a separate legacy field, which is the one the workflow drives
    private const string LEGACY_STATUS_FIELD_NAME = "Workflow Status";

    private const string FIELDS_QUERY =
        "query($projectId:ID!){node(id:$projectId){... on ProjectV2{fields(first:100){nodes{... on ProjectV2SingleSelectField{id name options{id name}}}}}}}";

    private readonly IRepositoryProjectCache _cache;
    private readonly IGitHubGraphQlClient _graphQlClient;

    public WorkflowStatusResolver(IGitHubGraphQlClient graphQlClient, IRepositoryProjectCache cache)
    {
        this._graphQlClient = graphQlClient;
        this._cache = cache;
    }

    public async ValueTask<ResolvedStatusOption> ResolveAsync(
        string repository,
        WorkflowProject project,
        string state,
        CancellationToken cancellationToken
    )
    {
        string canonicalState = WorkflowStates.Canonicalise(state);
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));

        WorkflowStatusField? cached = this._cache.GetStatusField(validRepository);

        if (cached is not null)
        {
            string? cachedOptionId = cached.FindOptionId(canonicalState);

            if (cachedOptionId is not null)
            {
                return new(FieldId: cached.FieldId, FieldName: cached.FieldName, OptionId: cachedOptionId);
            }

            // The option may have been added or renamed on the board since the field was cached, so read it again once
            this._cache.Invalidate(validRepository);
        }

        WorkflowStatusField statusField = await this.ReadStatusFieldAsync(
            project: project,
            cancellationToken: cancellationToken
        );

        this._cache.SetStatusField(repository: validRepository, statusField: statusField);

        string optionId =
            statusField.FindOptionId(canonicalState)
            ?? throw new StatusOptionMissingException(
                $"project {project.Number} has no '{canonicalState}' option on its {statusField.FieldName} field"
            );

        return new(FieldId: statusField.FieldId, FieldName: statusField.FieldName, OptionId: optionId);
    }

    private async ValueTask<WorkflowStatusField> ReadStatusFieldAsync(
        WorkflowProject project,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyList<WorkflowStatusField> fields = await this.ReadFieldsAsync(
            project: project,
            cancellationToken: cancellationToken
        );

        return FindField(fields: fields, name: LEGACY_STATUS_FIELD_NAME)
            ?? FindField(fields: fields, name: STATUS_FIELD_NAME)
            ?? throw new WorkflowStatusFieldNotFoundException(
                $"project {project.Number} has no {STATUS_FIELD_NAME} or {LEGACY_STATUS_FIELD_NAME} field"
            );
    }

    private async ValueTask<IReadOnlyList<WorkflowStatusField>> ReadFieldsAsync(
        WorkflowProject project,
        CancellationToken cancellationToken
    )
    {
        try
        {
            FieldsQueryData data = await this._graphQlClient.ExecuteAsync(
                query: FIELDS_QUERY,
                variables: new FieldsQueryVariables(ProjectId: project.ProjectNodeId),
                variablesTypeInfo: GitHubJsonContext.Default.FieldsQueryVariables,
                dataTypeInfo: GitHubJsonContext.Default.FieldsQueryData,
                cancellationToken: cancellationToken
            );

            IReadOnlyList<SingleSelectFieldNode?> nodes =
                data.Node?.Fields?.Nodes ?? throw new GitHubInvalidResponseException("the project fields were missing");

            return SelectUsableFields(nodes);
        }
        catch (Exception exception)
            when (exception is GitHubGraphQlException or GitHubInvalidResponseException or HttpRequestException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new StatusFieldReadFailedException(
                message: $"could not read the status fields of project {project.Number}",
                innerException: exception
            );
        }
    }

    private static IReadOnlyList<WorkflowStatusField> SelectUsableFields(IReadOnlyList<SingleSelectFieldNode?> nodes)
    {
        List<WorkflowStatusField> fields = [];

        foreach (SingleSelectFieldNode? node in nodes)
        {
            // Fields of other types match the inline fragment as an empty object, so they arrive with no id or name
            if (node is { Id: { Length: > 0 } id, Name: { Length: > 0 } name })
            {
                fields.Add(new(FieldId: id, FieldName: name, OptionIds: ToOptionIds(node.Options)));
            }
        }

        return fields;
    }

    private static WorkflowStatusField? FindField(IReadOnlyList<WorkflowStatusField> fields, string name)
    {
        return fields.FirstOrDefault(field => StringComparer.Ordinal.Equals(x: field.FieldName, y: name));
    }

    private static IReadOnlyDictionary<string, string> ToOptionIds(IReadOnlyList<FieldOptionNode?>? options)
    {
        Dictionary<string, string> optionIds = [with(StringComparer.Ordinal)];

        foreach (FieldOptionNode? option in options ?? [])
        {
            if (option is { Id: { Length: > 0 } id, Name: { Length: > 0 } name })
            {
                // A duplicated option name keeps the first id GitHub listed, matching the board's display order
                optionIds.TryAdd(key: name, value: id);
            }
        }

        return optionIds;
    }
}
