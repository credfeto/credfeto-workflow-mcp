# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

<!--
Please ADD ALL Changes to the UNRELEASED SECTION and not a specific release
-->

## [Unreleased]
### Security
- Moved to .NET 11 rc.3 SDK so NativeAOT compiler no longer hits GHSA-p3jp-9v83-76w9 (#90)
### Added
- Dockerfile for building and running the MCP server
- issue_list MCP tool listing open issues by priority with Blocked/On Hold flags (#18)
- Added pr_ready_at MCP tool returning when a pull request was last marked ready for review (#51)
- Added priority parameter to the issue_edit and pr_edit MCP tools
- pr_comments returns updatedAt per comment and review; exactBodies returns raw bodies (#154)
- pr_list returns label names and, with closingIssues, the issues each pull request closes (#151)
- pr_view opt-in fields: review-requests, milestone, closing-issues, checks, commits (#153)
- Credfeto.Workflow.Mcp.Server: C# (.NET 11) MCP server skeleton replacing cfwf, with /ping
- Bearer token required on /mcp; GitHub calls host-restricted, paged, with a per-caller id cache
- Name validation and GitHub API path encoding for repos, numbers, ids, SHAs, branches, labels
- Workflow board resolver (IWorkflowBoardResolver) finding a repository's open Workflow project
- Added read-only pr_comments MCP tool returning all comments, reviews and review threads (#29)
- Added read-only branch_required_checks MCP tool listing a branch's required status checks (#24)
- Approval keyword matcher, ignoring Markdown quote lines, ported from cfwf (#9)
- Workflow board reader (IWorkflowBoardReader) reading an item's workflow status via GraphQL (#7)
- Board writer (IBoardWriter) adds items to the Workflow board and sets their Status (#6)
- Workflow status resolver maps board states to Status field and option ids, cached (#5)
- Title rules for issue and pull request titles, including Conventional Commits PR titles (#10)
- Leak checker service refusing tokens, host paths, control characters and private references
- Added read-only pr_view MCP tool returning a pull request's metadata (#26)
- Added read-only issue_view MCP tool returning an issue's title, state, labels and body (#16)
- Added read-only pr_checks MCP tool giving a passed, failed, pending or no-checks verdict (#25)
- Added read-only workflow_status_check MCP tool returning an item's Workflow board state (#13)
- Added workflow_status_set MCP tool to move issues and pull requests on the board (#12)
- Added read-only issue_comments MCP tool returning recent comment bodies, oldest first (#58)
- Added read-only pr_list MCP tool listing open pull requests by issue, head or label (#61)
- Added label_add and label_remove MCP tools to manage issue and pull request labels (#64)
- Added issue_close MCP tool to close issues as completed or not planned (#66)
- Added issue_create MCP tool creating labelled issues and placing them on the board (#15)
- Added pr_reply MCP tool posting one reply in an inline review comment's thread (#30)
- Added pr_create MCP tool opening draft pull requests and placing them on the board (#28)
- Added issue_search MCP tool searching issue titles and bodies by sanitised words (#19)
- Added read-only body_check MCP tool running the leak check on a body without posting it (#20)
- Added comment_add MCP tool posting leak-checked top-level comments (#31)
- Added issue_plan_status MCP tool reporting plan and approval state, no comment bodies (#17)
- Added closing_issue_labels MCP tool returning labels of issues a pull request closes (#14)
- Added optional GitHub:ApiVersion setting for the X-GitHub-Api-Version header (#37)
- Added read-only pr_search, code_search and repo_search MCP tools for text search (#59)
- Added pr_hand_off MCP tool handing finished pull requests off for review and auto-merge (#55)
- Added pr_edit MCP tool correcting a pull request's title and description, leak-checked (#44)
- Added issue_reopen, pr_close and pr_reopen MCP tools to change item state (#56)
- Added run_list, run_view and run_log MCP tools for Actions runs and their logs (#43)
- Added issue_edit MCP tool editing an issue's title and body, reporting each outcome
- Added pr_status MCP tool reading a PR's whole state in one call (#73)
- Added run_rerun MCP tool rerunning failed jobs on an open pull request head (#52)
- Added repo_list and workflow_list MCP tools listing repos and their workflows (#60)
- Added read-only pr_auto_merge_at MCP tool reporting when auto-merge was last enabled (#62)
- Added label_create MCP tool creating or updating standard repository labels (#63)
- Added read-only label_list MCP tool listing a repository's label names (#65)
- run_log gains grep, context, maxMatches and fromLine options to search failed-step logs (#110)
- Added a build-time --self-test switch run in the Docker build so a broken binary fails (#109)
- Added read-only repo_dependabot_alerts MCP tool listing Dependabot alerts (#112)
- Added read-only pr_diff MCP tool with diff, name-only and stat modes (#50)
- Added pr_review_comment MCP tool posting inline review comments on a pull request's diff (#67)
- Added workflow_run MCP tool dispatching allowlisted workflows from the default branch (#113)
- Added issue_add_sub_issue MCP tool linking a sub-issue to a parent issue (#57)
- Added repo_file MCP tool reading files or listing directories at a ref (#78)
- GitHub REST and GraphQL calls follow redirects only within the configured base path (#36)
- Added optional GitHub:UpstreamBasePath setting for proxied next-page and GraphQL links (#35)
- Added tag_view MCP tool returning the commit a tag points to and its type (#34)
- Added release_view MCP tool returning a repository's latest release (#32)
- Added optional GitHub:Board:Token fallback for adding new issues to the Workflow board (#71)
- Added read-only release_list MCP tool listing a repository's published releases (#33)
- Added repo_collaborators MCP tool listing a repository's collaborators and roles (#22)
- X-Mode header limits tools: Issues allows issue tools only, unknown modes are rejected (#139)
- Added read-only repo_view MCP tool reading default branch, visibility and merge settings (#21)
- Added auth_status MCP tool reporting the signed-in login, host and API version (#23)
- Added read-only branch_compare MCP tool reporting commits ahead and behind base (#149)
- pr_checks reports per-check required, reported and kind, plus notReported counts (#148)
- issue_view returns milestone and, with comments, every comment's author and times (#150)
- Added read-only workflow_board_items MCP tool listing every Workflow board item (#152)
- Added pr_sync_labels MCP tool adding closing issues' labels to a pull request (#155)
- Added read-only repo_advisories MCP tool listing published security advisories (#48)
- Added advisory_view MCP tool showing a GHSA advisory's severity and packages (#49)
- Added label_history MCP tool listing label adds and removes with actor and time (#53)
- Added read-only workflow_status_history MCP tool listing board status changes (#171)
- Added issue_post_plan MCP tool posting an implementation plan and setting Planning (#173)
- Added issue_block and pr_block MCP tools posting a blocking question and adding Blocked
- Added pr_take_back MCP tool returning a pull request to Development for more work (#175)
- Added pr_checks_wait MCP tool waiting a bounded time for checks to reach a verdict (#176)
- issue_create accepts assignees and assignSelf so issues are assigned on creation (#185)
- pr_view checks say if GitHub requires each, exactCheckNames option, commit_check_runs tool (#187)
### Fixed
- pr_checks reports a cancelled check as pending while a newer run is queued or running (#133)
- issue_edit, pr_edit, pr_sync_labels and issue_create report failed assignments (#185)
### Changed
- README: GitHub:ApiBaseUrl row now says the URL must have no query or fragment (#38)
- SDK - Updated DotNet SDK to 11.0.100
- run_rerun no longer refuses a run on attempt 3 or later; the attempt limit is removed (#146)
- Enabled runtime-async in all projects and moved the Dockerfile runtime image to .NET 11 rc.3 (#74)
- label_add and label_remove refuse pull requests in X-Mode Issues, as comment_add does (#142)
- tools/list is returned sorted by tool name in every mode, so clients see a stable order (#165)
- The leak check allows placeholder values such as changeme and REDACTED, so templates pass (#185)
- Tool tests compare tool names read from [McpServerTool] methods, not a counted total (#164)
- issue_edit and pr_edit refuse assignSelf when another user already holds the item (#129)
### Deprecated
### Removed
### Deployment Changes

<!--
Releases that have at least been deployed to staging, BUT NOT necessarily released to live.  Changes should be moved from [Unreleased] into here as they are merged into the appropriate release branch
-->

## [0.0.0] - Project created