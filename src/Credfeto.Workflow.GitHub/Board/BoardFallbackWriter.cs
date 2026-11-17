using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Board.LoggingExtensions;
using Credfeto.Workflow.GitHub.Cache;
using Credfeto.Workflow.GitHub.Configuration;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Json;
using Credfeto.Workflow.GitHub.Status;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Credfeto.Workflow.GitHub.Board;

// Every request goes through the board client, so it only ever reads the repository's Workflow project and its Status
// field, and only ever adds an item and sets it to Not Started
[DebuggerDisplay("IsConfigured: {IsConfigured}")]
public sealed class BoardFallbackWriter : IBoardFallbackWriter
{
    private readonly IGitHubGraphQlClient _graphQlClient;
    private readonly ILogger<BoardFallbackWriter> _logger;
    private readonly IOptions<GitHubOptions> _options;
    private readonly IWorkflowBoardResolver _boardResolver;
    private readonly IWorkflowStatusResolver _statusResolver;

    public BoardFallbackWriter(
        IGitHubGraphQlClient graphQlClient,
        IOptions<GitHubOptions> options,
        ILogger<BoardFallbackWriter> logger
    )
    {
        this._graphQlClient = graphQlClient;
        this._options = options;
        this._logger = logger;

        UncachedRepositoryProjectCache cache = new();
        this._boardResolver = new WorkflowBoardResolver(graphQlClient: graphQlClient, cache: cache);
        this._statusResolver = new WorkflowStatusResolver(graphQlClient: graphQlClient, cache: cache);
    }

    public bool IsConfigured => this._options.Value.Board.IsConfigured;

    public async ValueTask CheckNotStartedAsync(string repository, CancellationToken cancellationToken)
    {
        await this.ResolveNotStartedAsync(repository: repository, cancellationToken: cancellationToken);

        this._logger.LogResolvedWithBoardToken(repository);
    }

    public async ValueTask AddAsNotStartedAsync(
        string repository,
        string contentNodeId,
        Uri url,
        CancellationToken cancellationToken
    )
    {
        if (!BoardMutations.IsNodeId(contentNodeId))
        {
            throw new BoardWriteException($"unexpected node id for {url}");
        }

        try
        {
            (WorkflowProject project, ResolvedStatusOption option) = await this.ResolveNotStartedAsync(
                repository: repository,
                cancellationToken: cancellationToken
            );

            string itemId = await this.AddItemAsync(
                project: project,
                contentNodeId: contentNodeId,
                cancellationToken: cancellationToken
            );

            await this.SetNotStartedAsync(
                project: project,
                option: option,
                itemId: itemId,
                cancellationToken: cancellationToken
            );

            this._logger.LogAddedWithBoardToken(url: url, projectNumber: project.Number);
        }
        catch (Exception exception) when (IsBoardFailure(exception))
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new BoardWriteException(
                message: $"could not add {url} to the Workflow board as Not Started with the board token",
                innerException: exception
            );
        }
    }

    private async ValueTask<(WorkflowProject Project, ResolvedStatusOption Option)> ResolveNotStartedAsync(
        string repository,
        CancellationToken cancellationToken
    )
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));

        WorkflowProject project = await this._boardResolver.ResolveAsync(
            repository: validRepository,
            cancellationToken: cancellationToken
        );

        ResolvedStatusOption option = await this._statusResolver.ResolveAsync(
            repository: validRepository,
            project: project,
            state: WorkflowStates.NOT_STARTED,
            cancellationToken: cancellationToken
        );

        return (project, option);
    }

    private async ValueTask<string> AddItemAsync(
        WorkflowProject project,
        string contentNodeId,
        CancellationToken cancellationToken
    )
    {
        AddItemMutationData data = await this._graphQlClient.ExecuteAsync(
            query: BoardMutations.ADD_ITEM,
            variables: new AddItemMutationVariables(Project: project.ProjectNodeId, Content: contentNodeId),
            variablesTypeInfo: GitHubJsonContext.Default.AddItemMutationVariables,
            dataTypeInfo: GitHubJsonContext.Default.AddItemMutationData,
            cancellationToken: cancellationToken
        );

        string? itemId = data.AddProjectV2ItemById?.Item?.Id;

        return BoardMutations.IsNodeId(itemId)
            ? itemId
            : throw new GitHubInvalidResponseException("the added project item was missing");
    }

    private async ValueTask SetNotStartedAsync(
        WorkflowProject project,
        ResolvedStatusOption option,
        string itemId,
        CancellationToken cancellationToken
    )
    {
        UpdateItemMutationData data = await this._graphQlClient.ExecuteAsync(
            query: BoardMutations.UPDATE_ITEM,
            variables: new UpdateItemMutationVariables(
                Project: project.ProjectNodeId,
                Item: itemId,
                Field: option.FieldId,
                Option: option.OptionId
            ),
            variablesTypeInfo: GitHubJsonContext.Default.UpdateItemMutationVariables,
            dataTypeInfo: GitHubJsonContext.Default.UpdateItemMutationData,
            cancellationToken: cancellationToken
        );

        if (data.UpdateProjectV2ItemFieldValue?.ProjectV2Item?.Id is null)
        {
            throw new GitHubInvalidResponseException("the updated project item was missing");
        }
    }

    private static bool IsBoardFailure(Exception exception)
    {
        return exception
            is BoardTokenMissingException
                or GitHubGraphQlException
                or GitHubInvalidResponseException
                or HttpRequestException
                or WorkflowBoardNotFoundException
                or WorkflowBoardAmbiguousException
                or WorkflowBoardInvalidException
                or StatusFieldReadFailedException
                or WorkflowStatusFieldNotFoundException
                or StatusOptionMissingException
                or AmbiguousStatusOptionException;
    }
}
