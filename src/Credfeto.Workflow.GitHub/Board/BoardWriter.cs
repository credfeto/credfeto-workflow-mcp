using System;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Board.LoggingExtensions;
using Credfeto.Workflow.GitHub.Http;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Json;
using Microsoft.Extensions.Logging;

namespace Credfeto.Workflow.GitHub.Board;

public sealed partial class BoardWriter : IBoardWriter
{
    private const int REGEX_TIMEOUT_MILLISECONDS = 1000;

    // The GraphQL client surfaces only the error messages, so a stale cached item id is recognised by its message
    private const string STALE_NODE = "Could not resolve to a node";

    private const string ISSUE_NODE_ID_QUERY =
        "query($o:String!,$r:String!,$n:Int!){repository(owner:$o,name:$r){item:issue(number:$n){id}}}";

    private const string PULL_REQUEST_NODE_ID_QUERY =
        "query($o:String!,$r:String!,$n:Int!){repository(owner:$o,name:$r){item:pullRequest(number:$n){id}}}";

    private const string ADD_ITEM_MUTATION =
        "mutation($p:ID!,$c:ID!){addProjectV2ItemById(input:{projectId:$p,contentId:$c}){item{id}}}";

    private const string UPDATE_ITEM_MUTATION =
        "mutation($p:ID!,$i:ID!,$f:ID!,$o:String!){updateProjectV2ItemFieldValue(input:{projectId:$p,itemId:$i,fieldId:$f,value:{singleSelectOptionId:$o}}){projectV2Item{id}}}";

    private readonly IRepositoryProjectCache _cache;
    private readonly IGitHubGraphQlClient _graphQlClient;
    private readonly ILogger<BoardWriter> _logger;

    public BoardWriter(IGitHubGraphQlClient graphQlClient, IRepositoryProjectCache cache, ILogger<BoardWriter> logger)
    {
        this._graphQlClient = graphQlClient;
        this._cache = cache;
        this._logger = logger;
    }

    public async ValueTask SetItemStatusAsync(
        string owner,
        string repo,
        WorkItemKind kind,
        int number,
        WorkflowProject project,
        string fieldId,
        string fieldName,
        string optionId,
        CancellationToken cancellationToken
    )
    {
        string repository = GitHubNames.Repository(value: $"{owner}/{repo}", parameterName: nameof(repo));

        if (number <= 0)
        {
            throw ParameterValidationException.For(parameterName: nameof(number), rule: "must be above 0");
        }

        BoardWriteRequest request = new(
            Owner: owner,
            Repo: repo,
            Repository: repository,
            Kind: kind,
            Number: number,
            Url: $"https://github.com/{repository}/{(kind == WorkItemKind.PULL_REQUEST ? "pull" : "issues")}/{number}",
            Project: project,
            FieldId: fieldId,
            FieldName: fieldName,
            OptionId: optionId
        );

        string? cachedItemId = this._cache.GetItemId(repository: repository, kind: kind, number: number);

        if (cachedItemId is null)
        {
            await this.AddAndSetStatusAsync(request: request, cancellationToken: cancellationToken);

            return;
        }

        try
        {
            await this.UpdateStatusAsync(request: request, itemId: cachedItemId, cancellationToken: cancellationToken);

            return;
        }
        catch (GitHubGraphQlException exception) when (IsStale(exception))
        {
            this._logger.LogStaleItemId(
                reference: request.Url,
                reason: UpstreamText.ReplaceLineBreaking(exception.Message)
            );
            this._cache.Invalidate(repository: repository);
        }
        catch (Exception exception)
            when (exception is GitHubGraphQlException or GitHubInvalidResponseException or HttpRequestException)
        {
            throw SetStatusFailed(request: request, exception: exception);
        }

        // Recovery adds the item once; a further failure is reported rather than retried again
        await this.AddAndSetStatusAsync(request: request, cancellationToken: cancellationToken);
    }

    private static bool IsStale(GitHubGraphQlException exception)
    {
        return exception.Errors.Any(error =>
            error.Contains(value: STALE_NODE, comparisonType: StringComparison.Ordinal)
        );
    }

    private async ValueTask AddAndSetStatusAsync(BoardWriteRequest request, CancellationToken cancellationToken)
    {
        string itemId = await this.AddItemAsync(request: request, cancellationToken: cancellationToken);

        this._cache.SetItemId(
            repository: request.Repository,
            kind: request.Kind,
            number: request.Number,
            itemId: itemId
        );

        try
        {
            await this.UpdateStatusAsync(request: request, itemId: itemId, cancellationToken: cancellationToken);
        }
        catch (Exception exception)
            when (exception is GitHubGraphQlException or GitHubInvalidResponseException or HttpRequestException)
        {
            throw SetStatusFailed(request: request, exception: exception);
        }
    }

    private async ValueTask<string> AddItemAsync(BoardWriteRequest request, CancellationToken cancellationToken)
    {
        string? itemId;

        try
        {
            string contentId = await this.ReadContentIdAsync(request: request, cancellationToken: cancellationToken);

            AddItemMutationData data = await this._graphQlClient.ExecuteAsync(
                query: ADD_ITEM_MUTATION,
                variables: new AddItemMutationVariables(Project: request.Project.ProjectNodeId, Content: contentId),
                variablesTypeInfo: GitHubJsonContext.Default.AddItemMutationVariables,
                dataTypeInfo: GitHubJsonContext.Default.AddItemMutationData,
                cancellationToken: cancellationToken
            );

            itemId = (
                data.AddProjectV2ItemById
                ?? throw new GitHubInvalidResponseException("the added project item was missing")
            )
                .Item
                ?.Id;
        }
        catch (Exception exception)
            when (exception is GitHubGraphQlException or GitHubInvalidResponseException or HttpRequestException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new BoardWriteException(
                message: $"could not add {request.Url} to project {request.Project.Number}",
                innerException: exception
            );
        }

        if (itemId is null || !ItemId().IsMatch(itemId))
        {
            throw new BoardWriteException($"unexpected item id returned for {request.Url}");
        }

        return itemId;
    }

    private async ValueTask<string> ReadContentIdAsync(BoardWriteRequest request, CancellationToken cancellationToken)
    {
        NodeIdQueryData data = await this._graphQlClient.ExecuteAsync(
            query: request.Kind == WorkItemKind.PULL_REQUEST ? PULL_REQUEST_NODE_ID_QUERY : ISSUE_NODE_ID_QUERY,
            variables: new StatusQueryVariables(Owner: request.Owner, Name: request.Repo, Number: request.Number),
            variablesTypeInfo: GitHubJsonContext.Default.StatusQueryVariables,
            dataTypeInfo: GitHubJsonContext.Default.NodeIdQueryData,
            cancellationToken: cancellationToken
        );

        return data.Repository?.Item?.Id ?? throw new GitHubInvalidResponseException("the work item id was missing");
    }

    private async ValueTask UpdateStatusAsync(
        BoardWriteRequest request,
        string itemId,
        CancellationToken cancellationToken
    )
    {
        UpdateItemMutationData data = await this._graphQlClient.ExecuteAsync(
            query: UPDATE_ITEM_MUTATION,
            variables: new UpdateItemMutationVariables(
                Project: request.Project.ProjectNodeId,
                Item: itemId,
                Field: request.FieldId,
                Option: request.OptionId
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

    private static BoardWriteException SetStatusFailed(BoardWriteRequest request, Exception exception)
    {
        // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
        return new(message: $"could not set the {request.FieldName} of {request.Url}", innerException: exception);
    }

    // \z rather than $ because in .NET $ also matches before a trailing newline, which would let one through
    [GeneratedRegex(
        pattern: @"^[A-Za-z0-9_=-]+\z",
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: REGEX_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex ItemId();
}
