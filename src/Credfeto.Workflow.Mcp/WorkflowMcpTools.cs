using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.Data.Models.Models;
using Credfeto.Workflow.GitHub.Interfaces;
using Credfeto.Workflow.GitHub.Interfaces.Exceptions;
using Credfeto.Workflow.GitHub.Interfaces.Validation;
using Credfeto.Workflow.GitHub.Runs;
using Credfeto.Workflow.GitHub.Search;
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

    // Never merges and never deletes the head branch, so a closed pull request can always be reopened
    [McpServerTool(
        Name = "pr_close",
        Title = "Pull request close",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true
    )]
    [Description(
        "Closes an open pull request without merging it or deleting its branch, posting the optional comment first; a pull request that is already closed is left unchanged and a merged one is refused"
    )]
    public static async Task<PullRequestStateChangeResponse> PrCloseAsync(
        [Description("The repository, as owner/name")] string repo,
        [Description("The pull request number")] int pr,
        IPullRequestService service,
        [Description("A comment to post on the pull request before closing it")] string? comment = null,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await service.CloseAsync(
                repository: repo,
                pullRequest: pr,
                comment: comment,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception) when (IsPullRequestStateChangeError(exception))
        {
            // Every one of these messages is fixed text or validated caller input, never upstream detail, so it is
            // safe to return
            throw new McpException(message: exception.Message, innerException: exception);
        }
    }

    [McpServerTool(
        Name = "pr_reopen",
        Title = "Pull request reopen",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true
    )]
    [Description(
        "Reopens a closed pull request, posting the optional comment first; a pull request that is already open is left unchanged and a merged one is refused"
    )]
    public static async Task<PullRequestStateChangeResponse> PrReopenAsync(
        [Description("The repository, as owner/name")] string repo,
        [Description("The pull request number")] int pr,
        IPullRequestService service,
        [Description("A comment to post on the pull request before reopening it")] string? comment = null,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await service.ReopenAsync(
                repository: repo,
                pullRequest: pr,
                comment: comment,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception) when (IsPullRequestStateChangeError(exception))
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

    [McpServerTool(Name = "closing_issue_labels", Title = "Closing issue labels", ReadOnly = true)]
    [Description(
        "Collects the labels of every issue a pull request closes, leaving out Blocked, On Hold and On-Hold in any case, and reports any closing issue whose labels could not be read"
    )]
    public static async Task<ClosingIssueLabelsResponse> ClosingIssueLabelsAsync(
        [Description("The repository, as owner/name")] string repo,
        [Description("The pull request number")] int pr,
        IClosingIssueLabelsService service,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await service.ReadAsync(repository: repo, pullRequest: pr, cancellationToken: cancellationToken);
        }
        catch (Exception exception)
            when (exception is ClosingIssueLabelsFailedException or ParameterValidationException)
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

    [McpServerTool(Name = "issue_plan_status", Title = "Issue plan status", ReadOnly = true)]
    [Description(
        "Reads whether an issue has an implementation plan comment and whether it is blocked, who posted the latest plan and when, and the author, time and approval keyword flag of every comment after it, without any comment body"
    )]
    public static async Task<IssuePlanStatus> IssuePlanStatusAsync(
        [Description("The repository, as owner/name")] string repo,
        [Description("The issue number")] int issue,
        IIssuePlanStatusReader reader,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await reader.ReadAsync(repository: repo, issue: issue, cancellationToken: cancellationToken);
        }
        catch (Exception exception)
            when (exception is IssueNotFoundException or IssueViewFailedException or ParameterValidationException)
        {
            // Every one of these messages is fixed text or validated caller input, never upstream detail, so it is
            // safe to return
            throw new McpException(message: exception.Message, innerException: exception);
        }
    }

    [McpServerTool(Name = "issue_search", Title = "Issue search", ReadOnly = true)]
    [Description(
        "Searches the titles and bodies of a repository's issues for text, returning the number, state, title and labels of each match; truncated is true when more matches exist than the limit"
    )]
    public static async Task<IssueSearchResult> IssueSearchAsync(
        [Description("The repository, as owner/name")] string repo,
        [Description("The words to search for in issue titles and bodies")] string text,
        IIssueSearchService service,
        [Description("Which issues to search: open, closed or all; open when not given")] string? state = null,
        [Description("The most matches to return, from 1 to 100")] int limit = IssueSearchQuery.DEFAULT_LIMIT,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await service.SearchAsync(
                repository: repo,
                text: text,
                state: IssueSearchQuery.State(value: state, parameterName: nameof(state)),
                limit: limit,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception)
            when (exception
                    is IssueSearchRefusedException
                        or IssueSearchFailedException
                        or RepositoryNotFoundException
                        or ParameterValidationException
            )
        {
            // Every one of these messages is fixed text or validated caller input, never upstream detail, so it is
            // safe to return
            throw new McpException(message: exception.Message, innerException: exception);
        }
    }

    [McpServerTool(Name = "pr_search", Title = "Pull request search", ReadOnly = true)]
    [Description(
        "Searches the titles and bodies of a repository's pull requests for text, returning the number, state, head branch and title of each match; truncated is true when more matches exist than the limit"
    )]
    public static async Task<PrSearchResult> PrSearchAsync(
        [Description("The repository, as owner/name")] string repo,
        [Description("The words to search for in pull request titles and bodies")] string text,
        IPrSearchService service,
        [Description("Which pull requests to search: open, closed, merged or all; open when not given")]
            string? state = null,
        [Description("The most matches to return, from 1 to 100")] int limit = IssueSearchQuery.DEFAULT_LIMIT,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await service.SearchAsync(
                repository: repo,
                text: text,
                state: PrSearchQuery.State(value: state, parameterName: nameof(state)),
                limit: limit,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception)
            when (exception
                    is IssueSearchRefusedException
                        or PrSearchFailedException
                        or RepositoryNotFoundException
                        or ParameterValidationException
            )
        {
            // Every one of these messages is fixed text or validated caller input, never upstream detail, so it is
            // safe to return
            throw new McpException(message: exception.Message, innerException: exception);
        }
    }

    [McpServerTool(Name = "code_search", Title = "Code search", ReadOnly = true)]
    [Description(
        "Searches the code of a repository's default branch for text, returning the path and match count of each matching file, sorted by path; truncated is true when more matches exist than the limit"
    )]
    public static async Task<CodeSearchResult> CodeSearchAsync(
        [Description("The repository, as owner/name")] string repo,
        [Description("The words to search for in the repository's code")] string text,
        ICodeSearchService service,
        [Description("The most matches to return, from 1 to 100")] int limit = IssueSearchQuery.DEFAULT_LIMIT,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await service.SearchAsync(
                repository: repo,
                text: text,
                limit: limit,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception)
            when (exception
                    is IssueSearchRefusedException
                        or CodeSearchFailedException
                        or RepositoryNotFoundException
                        or ParameterValidationException
            )
        {
            // Every one of these messages is fixed text or validated caller input, never upstream detail, so it is
            // safe to return
            throw new McpException(message: exception.Message, innerException: exception);
        }
    }

    [McpServerTool(Name = "repo_search", Title = "Repository search", ReadOnly = true)]
    [Description(
        "Searches the names and descriptions of an owner's repositories for text, returning the name, visibility, status and default branch of each match, sorted by name; truncated is true when more matches exist than the limit"
    )]
    public static async Task<RepoSearchResult> RepoSearchAsync(
        [Description("The user or organisation that owns the repositories")] string owner,
        [Description("The words to search for in repository names and descriptions")] string text,
        IRepoSearchService service,
        [Description("Whether to include archived repositories; false when not given")] bool includeArchived = false,
        [Description("The most matches to return, from 1 to 100")] int limit = IssueSearchQuery.DEFAULT_LIMIT,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await service.SearchAsync(
                owner: owner,
                text: text,
                includeArchived: includeArchived,
                limit: limit,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception)
            when (exception
                    is IssueSearchRefusedException
                        or RepoSearchFailedException
                        or OwnerNotFoundException
                        or ParameterValidationException
            )
        {
            // Every one of these messages is fixed text or validated caller input, never upstream detail, so it is
            // safe to return
            throw new McpException(message: exception.Message, innerException: exception);
        }
    }

    [McpServerTool(Name = "issue_list", Title = "Issue list", ReadOnly = true)]
    [Description(
        "Lists a repository's open issues, excluding pull requests, most urgent priority first and then by number, with each issue's priority, blocked and on-hold flags, assignees, title and labels"
    )]
    public static async Task<IssueListResponse> IssueListAsync(
        [Description("The repository, as owner/name")] string repo,
        IIssueListService service,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await service.ListOpenIssuesAsync(repository: repo, cancellationToken: cancellationToken);
        }
        catch (Exception exception)
            when (exception is RepositoryNotFoundException or IssueListFailedException or ParameterValidationException)
        {
            // Every one of these messages is fixed text or validated caller input, never upstream detail, so it is
            // safe to return
            throw new McpException(message: exception.Message, innerException: exception);
        }
    }

    [McpServerTool(Name = "run_list", Title = "Run list", ReadOnly = true)]
    [Description(
        "Lists a repository's GitHub Actions runs for one branch or one commit, newest first, started by any event, with each run's id, workflow, status, conclusion, head SHA, event and creation time"
    )]
    public static async Task<RunListResponse> RunListAsync(
        [Description("The repository, as owner/name")] string repo,
        IRunListService service,
        [Description("Only the runs whose head branch is exactly this one; give exactly one of branch or sha")]
            string? branch = null,
        [Description("Only the runs whose head commit is exactly this one, as 40 hexadecimal digits")]
            string? sha = null,
        [Description("Only the runs of this workflow: its file name, such as build.yml, or its name")]
            string? workflow = null,
        [Description("The most runs to return, from 1 to 50")] int limit = RunQuery.DEFAULT_LIMIT,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await service.ListAsync(
                repository: repo,
                branch: branch,
                sha: sha,
                workflow: workflow,
                limit: limit,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception)
            when (exception is WorkflowNotFoundException or RunListFailedException or ParameterValidationException)
        {
            // Every one of these messages is fixed text or validated caller input, never upstream detail, so it is
            // safe to return
            throw new McpException(message: exception.Message, innerException: exception);
        }
    }

    [McpServerTool(Name = "run_view", Title = "Run view", ReadOnly = true)]
    [Description(
        "Reads one GitHub Actions run, its status, conclusion, head commit and branch, workflow, event, attempt and creation time, and the jobs of its latest attempt sorted by id"
    )]
    public static async Task<RunViewResponse> RunViewAsync(
        [Description("The repository, as owner/name")] string repo,
        [Description("The run's id, digits only")] string run,
        IRunViewService service,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await service.ViewAsync(repository: repo, run: run, cancellationToken: cancellationToken);
        }
        catch (Exception exception)
            when (exception is RunNotFoundException or RunViewFailedException or ParameterValidationException)
        {
            // Every one of these messages is fixed text or validated caller input, never upstream detail, so it is
            // safe to return
            throw new McpException(message: exception.Message, innerException: exception);
        }
    }

    [McpServerTool(Name = "run_log", Title = "Run log", ReadOnly = true)]
    [Description(
        "Reads the last lines of the logs of a completed GitHub Actions run's failed steps, from its latest attempt, with each line made printable, a line holding a token redacted and long secrets masked; the outcome is pending while the run or job has not completed and not found when the run or job is not in the repository"
    )]
    public static async Task<RunLogResponse> RunLogAsync(
        [Description("The repository, as owner/name")] string repo,
        [Description("The run's id, digits only")] string run,
        IRunLogService service,
        [Description("Only this job of the run: its id, digits only")] string? job = null,
        [Description("The most of the last lines of each failed step to keep, from 1 to 2000")]
            int lines = RunQuery.DEFAULT_LINES,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await service.ReadFailedAsync(
                repository: repo,
                run: run,
                job: job,
                lines: lines,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception) when (exception is RunLogFailedException or ParameterValidationException)
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

    [McpServerTool(
        Name = "issue_reopen",
        Title = "Issue reopen",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true
    )]
    [Description(
        "Reopens a closed issue, posting the optional comment first; an issue that is already open is left unchanged and the comment is not posted"
    )]
    public static async Task<IssueReopenResponse> IssueReopenAsync(
        [Description("The repository, as owner/name")] string repo,
        [Description("The issue number")] int issue,
        IIssueService service,
        [Description("A comment to post on the issue before reopening it")] string? comment = null,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await service.ReopenAsync(
                repository: repo,
                issue: issue,
                comment: comment,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception)
            when (exception
                    is IssueNotFoundException
                        or IssueReopenRefusedException
                        or IssueReopenFailedException
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

    // Destructive because the title and body given replace the ones the issue had; idempotent because a repeat call
    // finds the issue already as asked and writes nothing
    [McpServerTool(Name = "issue_edit", Title = "Issue edit", ReadOnly = false, Destructive = true, Idempotent = true)]
    [Description(
        "Edits an issue in one write: replaces its title and body and adds the signed-in account to its assignees, keeping the others; the title and body are leak checked before anything is written. Returns Edited or Unchanged with the issue URL, Refused with the rule and line, NotFound (a pull request number is not found) or Failed"
    )]
    public static async Task<IssueEditResult> IssueEditAsync(
        [Description("The repository, as owner/name")] string repo,
        [Description("The issue number")] int issue,
        IIssueEditService service,
        [Description(
            "The new issue title; in credfeto and funfair-tech repositories it must be \"prefix: text\" or \"prefix(name): text\" with a lower-case prefix"
        )]
            string? title = null,
        [Description("The new issue body, in Markdown")] string? body = null,
        [Description("Whether to add the signed-in account to the issue's assignees")] bool assignSelf = false,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await service.EditAsync(
                repository: repo,
                issue: issue,
                title: title,
                body: body,
                assignSelf: assignSelf,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception)
            when (exception
                    is ParameterValidationException
                        or RepositoryNotFoundException
                        or RepositoryVisibilityLookupFailedException
                        or LeakCheckFailedException
            )
        {
            // Every one of these messages is fixed text or validated caller input, never the title, body or upstream
            // detail, so it is safe to return
            throw new McpException(message: exception.Message, innerException: exception);
        }
    }

    [McpServerTool(
        Name = "pr_reply",
        Title = "Pull request reply",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false
    )]
    [Description(
        "Replies in the thread of an inline review comment on a pull request, refusing a reply that contains a secret; a reply to a reply goes to the top comment of its thread"
    )]
    public static async Task<PrReplyResult> PrReplyAsync(
        [Description("The repository, as owner/name")] string repo,
        [Description("The pull request number")] int pr,
        [Description("The id of the inline review comment to reply to")] string commentId,
        [Description("The reply text")] string body,
        IPrReplyService service,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await service.ReplyAsync(
                repository: repo,
                pullRequest: pr,
                commentId: commentId,
                body: body,
                cancellationToken: cancellationToken
            );
        }
        catch (ParameterValidationException exception)
        {
            // Every one of these messages is fixed text or validated caller input, never upstream detail, so it is
            // safe to return
            throw new McpException(message: exception.Message, innerException: exception);
        }
    }

    // Not idempotent: every successful call posts a new comment
    [McpServerTool(
        Name = "comment_add",
        Title = "Comment add",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false
    )]
    [Description(
        "Posts a top-level comment on a pull request or an issue and returns its URL, refusing a body that fails the leak check or carries an approval keyword outside a Markdown quote; returns the outcome Posted, Refused with the rule and line, NotPosted or MayHaveBeenPosted"
    )]
    public static async Task<CommentAddResult> CommentAddAsync(
        [Description("The repository, as owner/name")] string repo,
        [Description("The comment text, in Markdown")] string body,
        ICommentAddService service,
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
                body: body,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception)
            when (exception
                    is ParameterValidationException
                        or RepositoryNotFoundException
                        or RepositoryVisibilityLookupFailedException
                        or LeakCheckFailedException
            )
        {
            // Every one of these messages is fixed text or validated caller input, never the body or upstream detail,
            // so it is safe to return
            throw new McpException(message: exception.Message, innerException: exception);
        }
    }

    // Not idempotent: a call for a branch with no open pull request creates one
    [McpServerTool(
        Name = "pr_create",
        Title = "Pull request create",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false
    )]
    [Description(
        "Opens a draft pull request from a pushed branch and puts it on the repository's Workflow board as Development, or returns the open pull request for the branch, moving it forward to Development without changing its title or body; the title and body are checked before anything is created"
    )]
    public static async Task<PrCreateResponse> PrCreateAsync(
        [Description("The repository, as owner/name")] string repo,
        [Description("The pushed branch to open the pull request from")] string head,
        [Description(
            "The pull request title; in credfeto and funfair-tech repositories it must be \"prefix: text\" or \"prefix(name): text\" with a lower-case prefix"
        )]
            string title,
        [Description("The pull request body, in Markdown")] string body,
        IPrCreateService service,
        [Description("The branch to merge into; the repository's default branch when not given")] string? @base = null,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await service.CreateAsync(
                repository: repo,
                head: head,
                baseBranch: @base,
                title: title,
                body: body,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception) when (IsPrCreateError(exception))
        {
            // Every one of these messages is fixed text, validated caller input or, once the pull request exists, its
            // URL, never upstream detail, so it is safe to return
            throw new McpException(message: exception.Message, innerException: exception);
        }
    }

    // Not idempotent: every successful call posts a new review record
    [McpServerTool(
        Name = "pr_hand_off",
        Title = "Pull request hand off",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false
    )]
    [Description(
        "Hands a pull request off for human review: sets its final title and description, moves it to Human Review on the Workflow board, marks it ready, posts the review record naming the head and turns on auto-merge as a merge commit; refuses before writing anything unless the pull request is open, has no Blocked or On Hold label and every required check has passed. Returns HandedOff with auto-merge On, Unavailable or NotArmed, Refused with the rule, NotFound, NotHandedOff or MayHaveBeenHandedOff with the steps already made"
    )]
    public static async Task<PrHandOffResult> PrHandOffAsync(
        [Description("The repository, as owner/name")] string repo,
        [Description("The pull request number")] int pr,
        [Description(
            "The final pull request title, a Conventional Commits title such as \"feat(scope): text\" or \"fix: text\""
        )]
            string title,
        [Description("The final pull request description, in Markdown; keep every Closes line")] string description,
        IPrHandOffService service,
        [Description("Extra text for the review record, in Markdown, added under its reviewed head line")]
            string? record = null,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await service.HandOffAsync(
                repository: repo,
                pullRequest: pr,
                title: title,
                description: description,
                record: record,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception) when (IsPrHandOffError(exception))
        {
            // Every one of these messages is fixed text or validated caller input, never the title, description,
            // record or upstream detail, so it is safe to return
            throw new McpException(message: exception.Message, innerException: exception);
        }
    }

    // Destructive because the title and body given replace the ones the pull request had; idempotent because a repeat
    // call finds the pull request already as asked and writes nothing
    [McpServerTool(
        Name = "pr_edit",
        Title = "Pull request edit",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true
    )]
    [Description(
        "Edits a pull request in one write: replaces its title and description and adds the signed-in account to its assignees, keeping the others; the title and description are leak checked before anything is written. Returns Edited or Unchanged with the pull request URL, Refused with the rule and line, NotFound, NotEdited or MayHaveBeenEdited"
    )]
    public static async Task<PrEditResult> PrEditAsync(
        [Description("The repository, as owner/name")] string repo,
        [Description("The pull request number")] int pr,
        IPrEditService service,
        [Description(
            "The new pull request title, a Conventional Commits title such as \"feat(scope): text\" or \"fix: text\""
        )]
            string? title = null,
        [Description("The new pull request description, in Markdown")] string? body = null,
        [Description("Whether to add the signed-in account to the pull request's assignees")] bool assignSelf = false,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await service.EditAsync(
                repository: repo,
                pullRequest: pr,
                title: title,
                body: body,
                assignSelf: assignSelf,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception)
            when (exception
                    is ParameterValidationException
                        or RepositoryNotFoundException
                        or RepositoryVisibilityLookupFailedException
                        or LeakCheckFailedException
            )
        {
            // Every one of these messages is fixed text or validated caller input, never the title, body or upstream
            // detail, so it is safe to return
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

    // Writes nothing: the only GitHub calls are the visibility lookups the private repository rule makes
    [McpServerTool(Name = "body_check", Title = "Body check", ReadOnly = true)]
    [Description(
        "Runs the leak check every write of a body runs, without posting anything, so a body can be tested before it is used; returns Passed, or Refused with the rule and line, never the matched text"
    )]
    public static async Task<BodyCheckResult> BodyCheckAsync(
        [Description("The repository the body is for, as owner/name")] string repo,
        [Description("The body to check, in Markdown")] string body,
        IBodyCheckService service,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await service.CheckAsync(repository: repo, body: body, cancellationToken: cancellationToken);
        }
        catch (Exception exception)
            when (exception
                    is ParameterValidationException
                        or RepositoryNotFoundException
                        or RepositoryVisibilityLookupFailedException
                        or LeakCheckFailedException
            )
        {
            // Every one of these messages is fixed text, never the body or upstream detail, so it is safe to return
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
        return IsBoardOrRepositoryError(exception)
            || exception is IssueCreateRefusedException or IssueCreateFailedException or IssueCreateBoardWriteException;
    }

    private static bool IsPrCreateError(Exception exception)
    {
        return IsBoardOrRepositoryError(exception)
            || exception
                is PrCreateRefusedException
                    or PrCreateFailedException
                    or PrCreateBoardWriteException
                    or PullRequestListFailedException;
    }

    private static bool IsPrHandOffError(Exception exception)
    {
        return IsBoardOrRepositoryError(exception)
            || exception
                is LeakCheckFailedException
                    or PullRequestNotFoundException
                    or CommitNotFoundException
                    or BranchNotFoundException
                    or RequiredChecksUnreadableException
                    or PrChecksUnreadableException;
    }

    // The failures shared by every tool that validates its input, resolves the repository and writes to its
    // Workflow board
    private static bool IsBoardOrRepositoryError(Exception exception)
    {
        return exception
            is ParameterValidationException
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

    private static bool IsPullRequestStateChangeError(Exception exception)
    {
        return exception
            is PullRequestNotFoundException
                or PullRequestStateChangeRefusedException
                or PullRequestStateChangeFailedException
                or ParameterValidationException;
    }
}
