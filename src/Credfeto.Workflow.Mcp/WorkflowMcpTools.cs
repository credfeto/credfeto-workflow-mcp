using System;
using System.Collections.Generic;
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

    [McpServerTool(Name = "pr_view", Title = "Pull request view", ReadOnly = true)]
    [Description(
        "Reads the requested fields of a pull request, in canonical order; with no fields every field except files and body, which can only be asked for on their own"
    )]
    public static async Task<PullRequestView> PrViewAsync(
        [Description("The repository, as owner/name")] string repo,
        [Description("The pull request number")] int pr,
        IPullRequestService service,
        [Description(
            "The fields to return: state, draft, title, url, base, head, head-sha, auto-merge, review-decision, merge-state, mergeable, created-at, merged-at, merge-commit, labels, assignees, author, commit-authors, files or body"
        )]
            IReadOnlyList<string>? fields = null,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await service.ViewAsync(
                repository: repo,
                pullRequest: pr,
                fields: fields,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception)
            when (exception
                    is PullRequestNotFoundException
                        or PullRequestViewFailedException
                        or ParameterValidationException
            )
        {
            // Every one of these messages is fixed text or validated caller input, never upstream detail, so it is
            // safe to return
            throw new McpException(message: exception.Message, innerException: exception);
        }
    }

    [McpServerTool(Name = "pr_list", Title = "Pull request list", ReadOnly = true)]
    [Description(
        "Lists the open pull requests of a repository in number order, keeping only those on the given head branch and carrying every given label, or fails rather than return a partial list"
    )]
    public static async Task<PullRequestListResponse> PrListAsync(
        [Description("The repository, as owner/name")] string repo,
        IPullRequestListService service,
        [Description("The issue number; a pull request whose head branch names it is flagged issue")] int? issue = null,
        [Description("Only pull requests whose head branch is exactly this name")] string? head = null,
        [Description("Only pull requests carrying every one of these labels; compared case-insensitively")]
            IReadOnlyList<string>? labels = null,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await service.ListAsync(
                repository: repo,
                issue: issue,
                head: head,
                labels: labels,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception)
            when (exception
                    is RepositoryNotFoundException
                        or PullRequestListFailedException
                        or ParameterValidationException
            )
        {
            // Every one of these messages is fixed text or validated caller input, never upstream detail, so it is
            // safe to return
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

    [McpServerTool(Name = "pr_checks", Title = "Pull request checks", ReadOnly = true)]
    [Description(
        "Judges the checks of a pull request, commit or branch head against the required checks of the branch whose rules apply, giving a passed, failed, pending or no-checks verdict with each check's outcome"
    )]
    public static async Task<PrChecksResponse> PrChecksAsync(
        [Description("The repository, as owner/name")] string repo,
        IPrChecksService service,
        [Description("The pull request number; give exactly one of pr, sha or branch")] int? pr = null,
        [Description("The full 40 character commit id; give exactly one of pr, sha or branch")] string? sha = null,
        [Description("The branch name, whose head is judged; give exactly one of pr, sha or branch")]
            string? branch = null,
        [Description("Judge every check that reported, not only the required ones")] bool all = false,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await service.ReadAsync(
                repository: repo,
                pullRequest: pr,
                sha: sha,
                branch: branch,
                all: all,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception)
            when (exception
                    is PullRequestNotFoundException
                        or CommitNotFoundException
                        or BranchNotFoundException
                        or RepositoryNotFoundException
                        or RequiredChecksUnreadableException
                        or PrChecksUnreadableException
                        or ParameterValidationException
            )
        {
            // Every one of these messages is fixed text or validated caller input, never upstream detail, so it is
            // safe to return
            throw new McpException(message: exception.Message, innerException: exception);
        }
    }

    [McpServerTool(Name = "issue_view", Title = "Issue view", ReadOnly = true)]
    [Description("Reads the title, state, labels, assignees and body of an issue")]
    public static async Task<IssueViewResponse> IssueViewAsync(
        [Description("The repository, as owner/name")] string repo,
        [Description("The issue number")] int issue,
        IIssueService service,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await service.ViewAsync(repository: repo, issue: issue, cancellationToken: cancellationToken);
        }
        catch (Exception exception)
            when (exception is IssueNotFoundException or IssueViewFailedException or ParameterValidationException)
        {
            // Every one of these messages is fixed text or validated caller input, never upstream detail, so it is
            // safe to return
            throw new McpException(message: exception.Message, innerException: exception);
        }
    }

    [McpServerTool(Name = "issue_comments", Title = "Issue comments", ReadOnly = true)]
    [Description(
        "Reads the comments of an issue or pull request, oldest first, keeping the most recent 100 that match the filters"
    )]
    public static async Task<IssueComments> IssueCommentsAsync(
        [Description("The repository, as owner/name")] string repo,
        [Description("The issue or pull request number")] int issue,
        IIssueService service,
        [Description("Only comments created at or after this UTC time, as yyyy-MM-ddTHH:mm:ssZ")] string? since = null,
        [Description("Only comments by this author, as a login or app/<slug>; compared case-insensitively")]
            string? author = null,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await service.CommentsAsync(
                repository: repo,
                issue: issue,
                since: since,
                author: author,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception)
            when (exception
                    is IssueNotFoundException
                        or IssueViewFailedException
                        or IssueCommentsIncompleteException
                        or ParameterValidationException
            )
        {
            // Every one of these messages is fixed text or validated caller input, never upstream detail, so it is
            // safe to return
            throw new McpException(message: exception.Message, innerException: exception);
        }
    }

    [McpServerTool(
        Name = "issue_close",
        Title = "Issue close",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true
    )]
    [Description(
        "Closes an issue as completed or not planned, posting the optional comment first; an issue that is already closed is left unchanged"
    )]
    public static async Task<IssueCloseResponse> IssueCloseAsync(
        [Description("The repository, as owner/name")] string repo,
        [Description("The issue number")] int issue,
        [Description("Why the issue is closed: completed or not-planned")] string reason,
        IIssueService service,
        [Description("A comment to post on the issue before closing it")] string? comment = null,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await service.CloseAsync(
                repository: repo,
                issue: issue,
                reason: reason,
                comment: comment,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception)
            when (exception
                    is IssueNotFoundException
                        or IssueCloseRefusedException
                        or IssueCloseFailedException
                        or ParameterValidationException
            )
        {
            // Every one of these messages is fixed text or validated caller input, never upstream detail, so it is
            // safe to return
            throw new McpException(message: exception.Message, innerException: exception);
        }
    }

    // Not idempotent: every successful call creates a new issue
    [McpServerTool(
        Name = "issue_create",
        Title = "Issue create",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false
    )]
    [Description(
        "Creates an issue with a priority label and optional labels, creating any missing label, and puts it on the repository's Workflow board as Not Started; everything is checked before anything is created"
    )]
    public static async Task<IssueCreateResponse> IssueCreateAsync(
        [Description("The repository, as owner/name")] string repo,
        [Description("The priority, in any case: Security, Urgent, High, Medium or Low")] string priority,
        [Description(
            "The issue title; in credfeto and funfair-tech repositories it must be \"prefix: text\" or \"prefix(name): text\" with a lower-case prefix"
        )]
            string title,
        [Description("The issue body, in Markdown")] string body,
        IIssueCreateService service,
        [Description(
            "Labels to add besides the priority; a standard label is written in its standard spelling, and a priority is refused"
        )]
            IReadOnlyList<string>? labels = null,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await service.CreateAsync(
                repository: repo,
                priority: priority,
                title: title,
                body: body,
                labels: labels,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception) when (IsIssueCreateError(exception))
        {
            // Every one of these messages is fixed text, validated caller input or, once the issue exists, its URL,
            // never upstream detail, so it is safe to return
            throw new McpException(message: exception.Message, innerException: exception);
        }
    }

    [McpServerTool(Name = "workflow_status_check", Title = "Workflow status check", ReadOnly = true)]
    [Description(
        "Reads the Workflow board status of a pull request or issue, giving found with the state, unset, or not on the board"
    )]
    public static async Task<WorkflowStatusCheckResult> WorkflowStatusCheckAsync(
        [Description("The repository, as owner/name")] string repo,
        IWorkflowStatusCheckService service,
        [Description("The pull request number; give exactly one of pr or issue")] int? pr = null,
        [Description("The issue number; give exactly one of pr or issue")] int? issue = null,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await service.CheckAsync(
                repository: repo,
                pullRequest: pr,
                issue: issue,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception)
            when (exception
                    is WorkflowBoardNotFoundException
                        or WorkflowBoardAmbiguousException
                        or WorkflowBoardInvalidException
                        or BoardReadFailedException
                        or ParameterValidationException
            )
        {
            // Every one of these messages is fixed text or validated caller input, never upstream detail, so it is
            // safe to return
            throw new McpException(message: exception.Message, innerException: exception);
        }
    }

    // Setting a state the item already holds leaves the board unchanged, and nothing is ever removed from the board
    [McpServerTool(
        Name = "workflow_status_set",
        Title = "Workflow status set",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true
    )]
    [Description(
        "Moves a pull request or issue to a workflow state on the repository's Workflow board, adding it to the board first if it is not on it"
    )]
    public static async Task<WorkflowStatusSetResponse> WorkflowStatusSetAsync(
        [Description("The repository, as owner/name")] string repo,
        [Description(
            "The workflow state, in any case: Not Started, Planning, Approved, Development, AI Simplify, AI Review, AI Security Review, AI Coverage, Human Review or Complete"
        )]
            string status,
        IWorkflowStatusSetService service,
        [Description("The pull request number; give exactly one of pr or issue")] int? pr = null,
        [Description("The issue number; give exactly one of pr or issue")] int? issue = null,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await service.SetAsync(
                repository: repo,
                pullRequest: pr,
                issue: issue,
                status: status,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception)
            when (exception
                    is ParameterValidationException
                        or UnknownWorkflowStateException
                        or WorkflowBoardNotFoundException
                        or WorkflowBoardAmbiguousException
                        or WorkflowBoardInvalidException
                        or WorkflowStatusFieldNotFoundException
                        or StatusFieldReadFailedException
                        or StatusOptionMissingException
                        or AmbiguousStatusOptionException
                        or BoardWriteException
            )
        {
            // Every one of these messages is fixed text or validated caller input, never upstream detail, so it is
            // safe to return
            throw new McpException(message: exception.Message, innerException: exception);
        }
    }

    [McpServerTool(Name = "label_add", Title = "Label add", ReadOnly = false, Destructive = false, Idempotent = true)]
    [Description(
        "Adds standard labels to one pull request or issue, one label per call so existing labels are kept, refusing priority labels, and returns the labels it now carries"
    )]
    public static async Task<LabelChangeResponse> LabelAddAsync(
        [Description("The repository, as owner/name")] string repo,
        [Description("The standard labels to add")] IReadOnlyList<string> labels,
        ILabelService service,
        [Description("The pull request number; give exactly one of pr or issue")] int? pr = null,
        [Description("The issue number; give exactly one of pr or issue")] int? issue = null,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await service.AddAsync(
                repository: repo,
                pullRequest: pr,
                issue: issue,
                labels: labels,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception) when (IsLabelChangeError(exception))
        {
            // Every one of these messages is fixed text or validated caller input, never upstream detail, so it is
            // safe to return
            throw new McpException(message: exception.Message, innerException: exception);
        }
    }

    [McpServerTool(
        Name = "label_remove",
        Title = "Label remove",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true
    )]
    [Description(
        "Removes standard labels from one pull request or issue, one label per call, refusing priority labels, On Hold, and Blocked unless this agent added it last, and returns the labels it still carries"
    )]
    public static async Task<LabelChangeResponse> LabelRemoveAsync(
        [Description("The repository, as owner/name")] string repo,
        [Description("The standard labels to remove")] IReadOnlyList<string> labels,
        ILabelService service,
        [Description("The pull request number; give exactly one of pr or issue")] int? pr = null,
        [Description("The issue number; give exactly one of pr or issue")] int? issue = null,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await service.RemoveAsync(
                repository: repo,
                pullRequest: pr,
                issue: issue,
                labels: labels,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception) when (IsLabelChangeError(exception))
        {
            // Every one of these messages is fixed text or validated caller input, never upstream detail, so it is
            // safe to return
            throw new McpException(message: exception.Message, innerException: exception);
        }
    }

    private static bool IsIssueCreateError(Exception exception)
    {
        return exception
            is ParameterValidationException
                or IssueCreateRefusedException
                or IssueCreateFailedException
                or IssueCreateBoardWriteException
                or RepositoryNotFoundException
                or RepositoryVisibilityLookupFailedException
                or WorkflowBoardNotFoundException
                or WorkflowBoardAmbiguousException
                or WorkflowBoardInvalidException
                or WorkflowStatusFieldNotFoundException
                or StatusFieldReadFailedException
                or StatusOptionMissingException
                or AmbiguousStatusOptionException;
    }

    private static bool IsLabelChangeError(Exception exception)
    {
        return exception
            is LabelRefusedException
                or LabelChangeFailedException
                or PullRequestNotFoundException
                or IssueNotFoundException
                or ParameterValidationException;
    }
}
