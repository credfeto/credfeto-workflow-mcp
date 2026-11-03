# Changelog
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

<!--
Please ADD ALL Changes to the UNRELEASED SECTION and not a specific release
-->

## [Unreleased]
### Security
### Added
- Credfeto.Workflow.Mcp.Server: a C# (.NET 11) MCP server skeleton replacing cfwf, exposing a /ping endpoint and an MCP ping tool
- Dockerfile for building and running the MCP server
- Bearer token requirement on /mcp, forwarded to the configured GitHub ApiBaseUrl through host-restricted REST and GraphQL clients with bounded paging, plus a per-caller-token, per-repo id cache so ids fetched with one caller's token are never served to another
- Name validation and GitHub API path encoding (owner/repo, numbers, comment ids, SHAs, branches, tags, labels, BranchPath)
- Workflow board resolver (IWorkflowBoardResolver) that finds the open 'Workflow' GitHub Project v2 for a repository, cached per repository
- Added read-only pr_comments MCP tool (repo, pr) that returns all comments, reviews and review threads with their inline comments for a pull request, fully paginated and count-verified, using the GitHub GraphQL client (#29)
- Added read-only branch_required_checks MCP tool (repo, branch) that lists the status checks a branch requires, merged from branch protection and rulesets, read through a shared IRequiredChecksReader, so workflows can see which checks must pass before merge (#24)
- Approval keyword matcher (refusal check returning first matching line, and yes/no detection) ported from cfwf, ignoring Markdown quote lines (#9)
- Workflow board reader (IWorkflowBoardReader) that reads the workflow status of an issue or pull request from the Workflow board via GraphQL, falling back to a paged listing of board items when a direct lookup is not available (#7)
- Board writer (IBoardWriter / BoardWriter) that adds an issue or pull request to the Workflow project board and sets its Status field via GraphQL, so board status can be updated programmatically (#6)
- Workflow status resolver (IWorkflowStatusResolver / WorkflowStatusResolver) that resolves a workflow state name from WorkflowStates (the ten board states in board order) to the project's Status or Workflow Status field id and matching option id, caching results per repository and re-resolving once when the cached field or option is stale, so board status updates no longer rely on hard-coded ids; plus a fields query example in docs/github.http (#5)
- Title rules for issue and pull request titles, checked before any GitHub call: no control characters, a visible character required (invisible characters do not count), a 256 character limit, owner-aware issue prefixes, and Conventional Commits pull request titles, with labels limited to 50 characters and no commas, so malformed titles are refused early with a fixed message (#10)
- Leak checker service refusing tokens, host paths, control characters, invalid UTF-8, over-long bodies and private repository references in outgoing text
- Added read-only pr_view MCP tool (repo, pr, optional fields) that returns a pull request's metadata, so workflows can read a pull request's state through the server instead of calling GitHub directly (#26)
- Added read-only issue_view MCP tool (repo, issue) that returns an issue's title, state, labels, assignees and body, treating pull request numbers as not found (#16)
- Added read-only pr_checks MCP tool (repo, one of pr/sha/branch, optional all) that judges the checks on a head commit in a single read and returns a verdict (passed, failed, pending, no checks) with per-check lines and counts (#25)
- Added read-only workflow_status_check MCP tool (repo, pr or issue) that returns the Workflow board state of an issue or pull request, with distinct unset and not-on-board outcomes (#13)
- Added workflow_status_set MCP tool (repo, one of pr/issue, status) that sets the Workflow board status of an issue or pull request, so agents can move work through the board without manual updates, with input validation and documentation (#12)
- Added read-only issue_comments MCP tool (repo, issue, optional since and author filters) that returns the comment bodies of an issue or pull request, oldest first, keeping the most recent 100 matches, so workflows can read the discussion on an issue or pull request through the server (#58)
- Added read-only pr_list MCP tool (repo, optional issue, head and labels filters) that lists open pull requests, mirroring cfwf pr list, so workflows can find the pull request for an issue, head branch or label set through the server (#61)
- Added label_add and label_remove MCP tools that add and remove labels on issues and pull requests, with the same validation and refusals as cfwf, so workflows can manage labels through the server (#64)
- Added support for closing issues (issue_close)
### Fixed
### Changed
### Deprecated
### Removed
### Deployment Changes

<!--
Releases that have at least been deployed to staging, BUT NOT necessarily released to live.  Changes should be moved from [Unreleased] into here as they are merged into the appropriate release branch
-->

## [0.0.0] - Project created