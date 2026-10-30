using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Credfeto.Workflow.Mcp;

[McpServerToolType]
public sealed class WorkflowMcpTools
{
    private const string PONG = "pong";

    [McpServerTool(Name = "ping", Title = "Ping")]
    public static PingResponse Ping()
    {
        return new(PONG);
    }

    [McpServerTool(Name = "pr_comments", Title = "Pull request comments", ReadOnly = true)]
    [Description(
        "Reads every top-level comment, review and inline review thread on a pull request, or fails rather than return a partial list"
    )]
    public static async Task<PullRequestComments> PrCommentsAsync(
        [Description("The repository, as owner/name")] string repo,
        [Description("The pull request number")] int pr,
        IPullRequestCommentsReader reader,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await reader.ReadAsync(repository: repo, pullRequest: pr, cancellationToken: cancellationToken);
        }
        catch (Exception exception)
            when (exception
                    is PullRequestNotFoundException
                        or PullRequestCommentsIncompleteException
                        or ParameterValidationException
            )
        {
            // Every one of these messages is fixed text that never echoes upstream detail, so it is safe to return
            throw new McpException(message: exception.Message, innerException: exception);
        }
    }

    [McpServerTool(Name = "branch_required_checks", Title = "Branch required checks", ReadOnly = true)]
    [Description(
        "Lists the status check names a branch requires before a merge, from branch protection and rulesets combined, or fails rather than return a partial list"
    )]
    public static async Task<BranchRequiredChecksResponse> BranchRequiredChecksAsync(
        [Description("The repository, as owner/name")] string repo,
        [Description("The branch name")] string branch,
        IRequiredChecksReader reader,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await reader.ReadRequiredCheckNamesAsync(
                repository: repo,
                branch: branch,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception)
            when (exception
                    is BranchNotFoundException
                        or RepositoryNotFoundException
                        or RequiredChecksUnreadableException
                        or ParameterValidationException
            )
        {
            // Every one of these messages is fixed text or validated caller input, never upstream detail, so it is
            // safe to return
            throw new McpException(message: exception.Message, innerException: exception);
        }
    }
}
