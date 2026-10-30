using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Models;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Json;

namespace Credfeto.Workflow.GitHub.Board;

public sealed partial class WorkflowBoardResolver : IWorkflowBoardResolver
{
    private const string WORKFLOW_BOARD_TITLE = "Workflow";

    // GitHub returns at most 100 projects per page, so this allows 10,000 linked projects before giving up
    private const int MAX_PAGES = 100;

    private const int REGEX_TIMEOUT_MILLISECONDS = 1000;

    private const string PROJECTS_QUERY =
        "query($owner:String!,$name:String!,$after:String){ repository(owner:$owner,name:$name){ projectsV2(first: 100, after:$after){ nodes{ id number title closed resourcePath } pageInfo{ hasNextPage endCursor } } } }";

    private readonly IRepositoryProjectCache _cache;
    private readonly IGitHubGraphQlClient _graphQlClient;

    public WorkflowBoardResolver(IGitHubGraphQlClient graphQlClient, IRepositoryProjectCache cache)
    {
        this._graphQlClient = graphQlClient;
        this._cache = cache;
    }

    public async ValueTask<WorkflowProject> ResolveAsync(string repository, CancellationToken cancellationToken)
    {
        string validRepository = GitHubNames.Repository(value: repository, parameterName: nameof(repository));

        WorkflowProject? cached = this._cache.GetProject(validRepository);

        if (cached is not null)
        {
            return cached;
        }

        IReadOnlyList<ProjectNode> candidates = await this.ReadWorkflowCandidatesAsync(
            repository: validRepository,
            cancellationToken: cancellationToken
        );

        WorkflowProject project = ToProject(
            node: SelectWorkflowNode(candidates: candidates, repository: validRepository),
            repository: validRepository
        );

        // Only a fully validated board is cached, so any failure above is retried on the next call
        this._cache.SetProject(repository: validRepository, project: project);

        return project;
    }

    public void Invalidate(string repository)
    {
        this._cache.Invalidate(repository);
    }

    private async ValueTask<IReadOnlyList<ProjectNode>> ReadWorkflowCandidatesAsync(
        string repository,
        CancellationToken cancellationToken
    )
    {
        // GitHubNames.Repository guarantees exactly one separator between a non-empty owner and name
        int separator = repository.IndexOf('/', StringComparison.Ordinal);
        string owner = repository[..separator];
        string name = repository[(separator + 1)..];

        try
        {
            return await this.ReadAllPagesAsync(
                owner: owner,
                name: name,
                repository: repository,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception)
            when (exception is GitHubGraphQlException or GitHubInvalidResponseException or HttpRequestException)
        {
            // The upstream detail stays in the inner exception for diagnostics; callers only see the fixed message
            throw new WorkflowBoardInvalidException(
                message: CouldNotReadMessage(repository),
                innerException: exception
            );
        }
    }

    private async ValueTask<IReadOnlyList<ProjectNode>> ReadAllPagesAsync(
        string owner,
        string name,
        string repository,
        CancellationToken cancellationToken
    )
    {
        List<ProjectNode> candidates = [];
        HashSet<string> seenCursors = [with(StringComparer.Ordinal)];
        string? after = null;

        for (int page = 0; page < MAX_PAGES; ++page)
        {
            ProjectsQueryData data = await this._graphQlClient.ExecuteAsync(
                query: PROJECTS_QUERY,
                variables: new ProjectsQueryVariables(Owner: owner, Name: name, After: after),
                variablesTypeInfo: GitHubJsonContext.Default.ProjectsQueryVariables,
                dataTypeInfo: GitHubJsonContext.Default.ProjectsQueryData,
                cancellationToken: cancellationToken
            );

            ProjectsConnection connection = data.Repository?.ProjectsV2 ?? throw CouldNotRead(repository);
            ProjectsPageInfo pageInfo = connection.PageInfo ?? throw CouldNotRead(repository);

            candidates.AddRange(SelectCandidates(connection.Nodes));

            if (!pageInfo.HasNextPage)
            {
                return candidates;
            }

            // The cursor comes from the upstream, so a missing or repeated one must fail rather than loop forever
            after = pageInfo.EndCursor;

            if (after is null || !seenCursors.Add(after))
            {
                throw CouldNotRead(repository);
            }
        }

        throw CouldNotRead(repository);
    }

    // A project whose closed flag is absent is treated as open, so a board is never hidden by a missing field
    private static IEnumerable<ProjectNode> SelectCandidates(IReadOnlyList<ProjectNode?>? nodes)
    {
        return (nodes ?? [])
            .OfType<ProjectNode>()
            .Where(node =>
                StringComparer.Ordinal.Equals(x: node.Title, y: WORKFLOW_BOARD_TITLE) && node.Closed != true
            );
    }

    private static ProjectNode SelectWorkflowNode(IReadOnlyList<ProjectNode> candidates, string repository)
    {
        return candidates.Count switch
        {
            0 => throw new WorkflowBoardNotFoundException(
                $"no project titled {WORKFLOW_BOARD_TITLE} is linked to {repository}"
            ),
            1 => candidates[0],
            _ => throw new WorkflowBoardAmbiguousException(
                $"more than one project titled {WORKFLOW_BOARD_TITLE} is linked to {repository}"
            ),
        };
    }

    private static WorkflowProject ToProject(ProjectNode node, string repository)
    {
        if (node.Id is null || node.Number is not >= 0 || !ProjectId().IsMatch(node.Id))
        {
            throw new WorkflowBoardInvalidException($"unexpected project id/number for {repository}");
        }

        Match path = node.ResourcePath is null ? Match.Empty : ProjectResourcePath().Match(node.ResourcePath);

        if (!path.Success)
        {
            throw new WorkflowBoardInvalidException($"unexpected project path for {repository}");
        }

        // The board can belong to a different user or organisation than the repository, so the owner is taken from
        // the project's own path rather than assumed from the repository
        return new WorkflowProject(
            ProjectNodeId: node.Id,
            Number: node.Number.GetValueOrDefault(),
            Owner: path.Groups["owner"].Value
        );
    }

    private static WorkflowBoardInvalidException CouldNotRead(string repository)
    {
        return new(CouldNotReadMessage(repository));
    }

    private static string CouldNotReadMessage(string repository)
    {
        return $"could not read the projects of {repository}";
    }

    // \z rather than $ because in .NET $ also matches before a trailing newline, which would let one through
    [GeneratedRegex(
        pattern: @"^[A-Za-z0-9_=-]+\z",
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: REGEX_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex ProjectId();

    [GeneratedRegex(
        pattern: @"^/(users|orgs)/(?<owner>[A-Za-z0-9_.-]+)/projects/[0-9]+\z",
        options: RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: REGEX_TIMEOUT_MILLISECONDS
    )]
    private static partial Regex ProjectResourcePath();
}
