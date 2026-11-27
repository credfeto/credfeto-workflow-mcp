# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

<!--
Please ADD ALL Changes to the UNRELEASED SECTION and not a specific release
-->

## [Unreleased]
### Security
- Moved to the .NET 11 rc.3 SDK so the NativeAOT compiler is clear of GHSA-p3jp-9v83-76w9 (#90)
### Added
- pr_comments returns updatedAt for every comment, plus an exactBodies option (#154)
- pr_list returns label names, plus a closingIssues option listing the issues each PR closes (#151)
- pr_view opt-in fields: review-requests, milestone, cross-repository, closing-issues, checks (#153)
- Credfeto.Workflow.Mcp.Server: a .NET 11 MCP server skeleton with /ping and an MCP ping tool
- Dockerfile for building and running the MCP server
- Bearer token on /mcp, forwarded only to the configured GitHub API, with a per-caller id cache
- Name validation and GitHub API path encoding for owners, repos, numbers, SHAs, branches and tags
- Workflow board resolver that finds a repository's open Workflow project, cached per repository
- Read-only pr_comments tool: every comment, review and review thread of a pull request (#29)
- Read-only branch_required_checks tool listing the checks a branch requires (#24)
- Approval keyword matcher ported from cfwf, ignoring Markdown quote lines (#9)
- Workflow board reader for the status of an issue or pull request, with a paged fallback (#7)
- Board writer that adds an issue or pull request to the Workflow board and sets its status (#6)
- Workflow status resolver mapping board state names to the project's field and option ids (#5)
- Title rules for issue and pull request titles, checked before any GitHub call (#10)
- Leak checker refusing tokens, host paths, control characters and private repository references
- Read-only pr_view tool returning a pull request's metadata (#26)
- Read-only issue_view tool returning an issue's title, state, labels, assignees and body (#16)
- Read-only pr_checks tool judging a head commit's checks as passed, failed, pending or none (#25)
- Read-only workflow_status_check tool returning the Workflow board state of an item (#13)
- workflow_status_set tool setting the Workflow board status of an issue or pull request (#12)
- Read-only issue_comments tool returning an issue's comments, oldest first (#58)
- Read-only pr_list tool listing open pull requests by issue, head branch or labels (#61)
- label_add and label_remove tools with the same validation and refusals as cfwf (#64)
- issue_close tool closing an issue as completed or not-planned (#66)
- issue_create tool creating an issue with its labels and putting it on the Workflow board (#15)
- pr_reply tool posting one reply in an inline review comment's thread (#30)
- pr_create tool opening a draft pull request and putting it on the Workflow board (#28)
- issue_search tool searching one repository's issues by sanitised words (#19)
- issue_list MCP tool listing open issues by priority with Blocked/On Hold flags (#18)
- Read-only body_check tool running the leak check on a body without posting it (#20)
- comment_add tool posting a leak-checked top-level comment (#31)
- issue_plan_status tool reporting an issue's plan comment, Blocked state and later comments (#17)
- closing_issue_labels tool returning the labels of the issues a pull request closes (#14)
- Optional GitHub:ApiVersion setting for the X-GitHub-Api-Version header (#37)
- Read-only pr_search, code_search and repo_search tools (#59)
- pr_hand_off tool that hands a finished pull request off for review and enables auto-merge (#55)
- pr_edit tool editing a pull request's leak-checked title and description (#44)
- issue_reopen, pr_close and pr_reopen tools, each with an optional comment (#56)
- run_list, run_view and run_log tools for GitHub Actions runs and failed-step logs (#43)
- issue_edit tool editing an issue's title and body and assigning the agent
- pr_status tool reading a pull request's whole state in one call, plus pr_view changed-files (#73)
- Added pr_ready_at MCP tool returning when a pull request was last marked ready for review (#51)
- run_rerun tool rerunning the failed jobs of a run on an open pull request head (#52)
- repo_list tool listing an owner's repositories and workflow_list listing a repo's workflows (#60)
- Read-only pr_auto_merge_at tool reporting when auto-merge was last enabled (#62)
- label_create tool creating or updating a standard repository label (#63)
- Read-only label_list tool listing a repository's label names (#65)
- run_log gains grep, context, maxMatches and fromLine parameters to search failed-step logs (#110)
- Build-time --self-test switch, run in the Docker image build (#109)
- Read-only repo_dependabot_alerts tool with optional state and severity filters (#112)
- Read-only pr_diff tool with diff, name-only and stat modes (#50)
- pr_review_comment tool posting an inline review comment on a pull request's diff (#67)
- workflow_run tool dispatching a workflow the repository has allowlisted (#113)
- issue_add_sub_issue tool linking a sub-issue to its parent issue (#57)
- repo_file tool reading a file or listing a directory at a ref (#78)
- REST and GraphQL calls follow redirects only to targets within the configured base path (#36)
- Optional GitHub:UpstreamBasePath setting for proxied next-page links and GraphQL (#35)
- tag_view tool returning the commit a tag points to and whether it is annotated (#34)
- release_view tool returning a repository's latest release (#32)
- Optional GitHub:Board:Token setting, a fallback for adding new issues to the board (#71)
- Added priority parameter to the issue_edit and pr_edit MCP tools
- Read-only release_list tool listing a repository's non-draft releases, newest first (#33)
- repo_collaborators tool listing a repository's collaborators and their roles (#22)
- X-Mode request header limiting the tools a client can list and call (#139)
- Read-only repo_view tool: a repository's default branch, visibility and merge methods (#21)
- auth_status tool reporting the GitHub login, host, API version and optional rate limits (#23)
- Read-only branch_compare tool: how far a head branch is ahead of and behind its base (#149)
- pr_checks says per check if it is required, reported and its kind, and counts running runs (#148)
- issue_view returns the milestone and, with the comments flag, every comment whole (#150)
- Read-only workflow_board_items tool listing every item on a Workflow board with its status (#152)
- pr_sync_labels tool copying a pull request's closing issues' labels onto it (#155)
- Read-only repo_advisories tool listing a repository's published security advisories (#48)
- advisory_view tool returning a GHSA advisory's severity and affected packages (#49)
- label_history tool listing when each label was added or removed on an item (#53)
- Read-only workflow_status_history tool listing an item's Workflow board status changes (#171)
- issue_post_plan tool posting a plan, adding Blocked and moving the issue to Planning (#173)
- issue_block and pr_block tools posting a blocking question and adding the Blocked label
- pr_take_back tool taking a pull request back from automation (#175)
- pr_checks_wait tool waiting a bounded time for a pull request's checks to settle (#176)
- pr_view checks say if GitHub requires each, exactCheckNames option, commit_check_runs tool (#187)
### Fixed
- pr_checks shows a cancelled check as pending while a newer run of it is still going (#133)
### Changed
- Tool tests compare tool names read from the [McpServerTool] methods instead of a count (#164)
- issue_edit and pr_edit refuse assignSelf when another user is already assigned (#129)
- README: GitHub:ApiBaseUrl row now says the URL must have no query or fragment (#38)
- Enabled runtime-async in all projects and moved the Docker runtime image to .NET 11 rc.3 (#74)
- SDK - Updated DotNet SDK to 11.0.100
- label_add and label_remove refuse pull requests in X-Mode Issues (#142)
- run_rerun no longer refuses a run on attempt 3 or later; the attempt limit is removed (#146)
- tools/list is returned sorted by tool name in every mode (#165)
### Deprecated
### Removed
### Deployment Changes

<!--
Releases that have at least been deployed to staging, BUT NOT necessarily released to live.  Changes should be moved from [Unreleased] into here as they are merged into the appropriate release branch
-->

## [0.0.0] - Project created