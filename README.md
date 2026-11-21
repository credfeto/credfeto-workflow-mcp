# credfeto-workflow-mcp

An MCP (Model Context Protocol) server that gives AI agents the GitHub workflow operations currently provided by the `cfwf` command-line tool.

| Branch  | Status                                                |
|---------|-------------------------------------------------------|
| main    | [![Build: Pre-Release][pre-release-img]][pre-release] |
| release | [![Build: Release][release-img]][release]             |

## Overview

credfeto-workflow-mcp is an ASP.NET Core server, published as a native AOT executable, that exposes workflow tools over the MCP Streamable HTTP transport so agents can call them directly instead of shelling out to `cfwf`.
The MCP transport is stateless, so every request stands alone.
The server currently provides the `auth_status`, `body_check`, `branch_compare`, `branch_required_checks`, `closing_issue_labels`, `code_search`, `comment_add`, `issue_add_sub_issue`, `issue_close`, `issue_comments`, `issue_create`, `issue_edit`, `issue_list`, `issue_plan_status`, `issue_reopen`, `issue_search`, `issue_view`, `label_add`, `label_create`, `label_list`, `label_remove`, `ping`, `pr_auto_merge_at`, `pr_checks`, `pr_close`, `pr_comments`, `pr_create`, `pr_diff`, `pr_edit`, `pr_hand_off`, `pr_list`, `pr_ready_at`, `pr_reopen`, `pr_reply`, `pr_review_comment`, `pr_search`, `pr_status`, `pr_sync_labels`, `pr_view`, `release_list`, `release_view`, `repo_advisories`, `repo_collaborators`, `repo_dependabot_alerts`, `repo_file`, `repo_list`, `repo_search`, `repo_view`, `run_list`, `run_log`, `run_rerun`, `run_view`, `tag_view`, `workflow_board_items`, `workflow_list`, `workflow_run`, `workflow_status_check` and `workflow_status_set` tools and a matching HTTP health check; the other workflow tools that replace `cfwf` follow in later releases.

## Quick Start

Build and run the server from the `src` folder (requires the .NET SDK pinned in `src/global.json`):

```bash
cd src
dotnet run --project Credfeto.Workflow.Mcp.Server/Credfeto.Workflow.Mcp.Server.csproj
```

Then check it responds:

```bash
curl http://127.0.0.1:8080/ping
```

## Usage

### Endpoints

| Endpoint    | Description                                                                                                                                               |
|-------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------|
| `GET /ping` | Anonymous health check. Returns `{"value":"pong"}`.                                                                                                       |
| `POST /mcp` | MCP Streamable HTTP endpoint (stateless). Requires `Authorization: Bearer <token>` (401 otherwise) and must accept `application/json, text/event-stream`. |

### Authentication

Every `/mcp` request must send a GitHub token as `Authorization: Bearer <token>`; a request without one is rejected with `401 Unauthorized` before any tool runs.
The server does not validate or store the token: it forwards it on each GitHub API call made for that request, and GitHub rejects tokens that are invalid or lack access.
The server never makes an unauthenticated GitHub call; with no caller token the call fails instead.
The one exception to forwarding the caller's token is the Workflow board step of `issue_create`: when `GitHub:Board:Token` (see [Configuration](#configuration)) is set and GitHub refuses the caller's token the board, that token adds the new issue to the board as Not Started, and the result carries a note saying so.

### Tool modes

An optional `X-Mode` request header limits the tools a request can list (`tools/list`) and call (`tools/call`):

| `X-Mode`       | Tools                                                                                                                                                                                                                                                                |
|----------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| none, or `All` | Every tool.                                                                                                                                                                                                                                                          |
| `Issues`       | `body_check`, `comment_add` (issues only), `issue_add_sub_issue`, `issue_comments`, `issue_create`, `issue_edit`, `issue_list`, `issue_plan_status`, `issue_search`, `issue_view`, `label_add` (issues only), `label_list`, `label_remove` (issues only) and `ping`. |

- Mode names match in any case, so `issues` and `ISSUES` are the same as `Issues`.
- Any other value is rejected with `400 Bad Request` and a JSON-RPC error naming the valid modes, rather than falling back to every tool. That includes an empty value, a list such as `Issues, All`, and more than one `X-Mode` header. So a typo can't give a caller more tools.
- A tool outside the mode is not listed, and calling it anyway fails before it runs with a JSON-RPC `-32602` error saying the tool is not available in that mode.
- In `Issues` mode, `comment_add` refuses `pr`. It looks `issue` up first and refuses a number that turns out to be a pull request. Nothing is posted in either case.
- In `Issues` mode, `label_add` and `label_remove` refuse `pr` in the same way, with an error naming the mode. They look `issue` up first and refuse a number that turns out to be a pull request. No label changes in either case. With no `X-Mode` header, or with `All`, both still label pull requests and issues.
- Each tool declares its modes with an `[InModes(...)]` attribute next to its `[McpServerTool]` attribute. A test fails when a tool has none.

### MCP tools

| Tool                     | Description                                                                                                                                                                                                                                   |
|--------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `auth_status`            | Read only. Takes an optional `rateLimit`; returns the caller's login, the MCP host and the GitHub API version, plus the core and GraphQL rate limits (remaining, limit, UTC reset) when asked. A bad token fails as `not authenticated`.      |
| `body_check`             | Read only. Takes `repo` (`owner/repo`) and `body`; runs the leak check without posting and returns `Passed`, or `Refused` with the rule, the line and a message, never the matched text. An empty body is an error.                           |
| `branch_compare`         | Read only. Takes `repo` (`owner/repo`), `base` and `head` branch names; returns the repository, both branches, `status` (`identical`, `ahead`, `behind` or `diverged`), `aheadBy` and `behindBy`, counted from the merge base.                |
| `branch_required_checks` | Read only. Takes `repo` (`owner/repo`) and `branch`; returns the repository, the branch and the names of the status checks it requires before a merge, from branch protection and rulesets combined. Fails rather than return a partial list. |
| `closing_issue_labels`   | Read only. Takes `repo` (`owner/repo`) and `pr`; returns the sorted, unique labels of the issues the pull request closes, without `Blocked`, `On Hold` or `On-Hold`, whether every issue was read (`complete`) and the failed references.     |
| `code_search`            | Read only. Takes `repo` (`owner/repo`), `text` and optional `limit` (1 to 100, default 30); searches the default branch's code and returns each matching file's path and match count, sorted by path, and whether more matched.               |
| `comment_add`            | Takes `repo` (`owner/repo`), `body` and exactly one of `pr` or `issue`; posts a top-level comment unless the leak check or an approval keyword outside a `>` quote refuses it; returns the outcome, a message and the comment URL.            |
| `issue_add_sub_issue`    | Takes `repo` (`owner/repo`), `issue` (the parent) and `subIssue`; links `subIssue` as a sub-issue of `issue` and returns both references, the outcome (`linked` or `already-linked`) and the URLs. Refused for an issue as its own sub-issue. |
| `issue_close`            | Takes `repo` (`owner/repo`), `issue`, `reason` (`completed` or `not-planned`) and optional `comment`, posted first; closes the issue and returns the outcome (`closed` or `already-closed`), reason and URL. Refused for an On-Hold issue.    |
| `issue_comments`         | Read only. Takes `repo` (`owner/repo`), `issue` or pull request number, optional `since` (`yyyy-MM-ddTHH:mm:ssZ`) and `author`; returns the 100 most recent matching comments, oldest first, bodies capped at 500 lines, and a summary.       |
| `issue_create`           | Takes `repo` (`owner/repo`), `priority`, `title`, `body` and optional `labels`; creates missing labels, then the issue, puts it on the Workflow board as Not Started and returns its URL. A board failure still names the URL.                |
| `issue_edit`             | Takes `repo` (`owner/repo`), `issue` and at least one of `title`, `body`, `priority` and `assignSelf`; sets them in one write, keeping other labels and assignees; returns the outcome, URL and any old and new priority.                     |
| `issue_list`             | Read only. Takes `repo` (`owner/repo`); returns the open issues, not pull requests, most urgent priority first, then by number, each with priority, blocked and on-hold flags, assignees, title and labels. Fails rather than list over 1000. |
| `issue_plan_status`      | Read only. Takes `repo` (`owner/repo`) and `issue`; returns whether it has an `## Implementation Plan` comment (latest wins) and is Blocked, its time and author, then each later comment's time, author, association, approval flag and URL. |
| `issue_reopen`           | Takes `repo` (`owner/repo`), `issue` and optional `comment`, posted first; reopens a closed issue and returns the outcome (`open` or `already-open`) and URL. An open issue is left as it is and the comment is not posted.                   |
| `issue_search`           | Read only. Takes `repo` (`owner/repo`), `text` and optional `state` (`open`, `closed` or `all`; default `open`) and `limit` (1 to 100, default 30); returns each matching issue's number, state, title and labels, and whether more matched.  |
| `issue_view`             | Read only. Takes `repo` (`owner/repo`), `issue` and optional `comments`; returns the title, state, labels, assignees, milestone (if any) and body, and with `comments` every comment with `updatedAt` and whole body. A PR is not found.      |
| `label_create`           | Takes `repo` (`owner/repo`), a standard label `name` (any case), `description` and `color` (6 hex digits, no `#`); creates or updates the label to its standard spelling, colour and text; returns `Created`, `Updated` or `Unchanged`.       |
| `label_list`             | Read only. Takes `repo` (`owner/repo`); returns the repository and the names of its labels, in GitHub's order. Fails rather than list over 1000 or return a partial list.                                                                     |
| `ping`                   | Returns `{"value":"pong"}`, the same payload as `GET /ping`.                                                                                                                                                                                  |
| `pr_auto_merge_at`       | Read only. Takes `repo` (`owner/repo`) and `pr`; reads its whole timeline and returns `pr` and `enabledAt` (UTC), the time auto-merge was last enabled, or `-` when it never was. A number that is not a pull request is not found.           |
| `pr_checks`              | Read only. Takes `repo`, one of `pr`/`sha`/`branch`, optional `all`; judges the head's checks against the required ones. Each check says `required`, `reported` and `kind` (`CheckRun`/`Status`); also `notReported` and `runningCheckRuns`.  |
| `pr_close`               | Takes `repo` (`owner/repo`), `pr` and optional `comment`, posted first; closes the pull request without merging or deleting its branch; returns the outcome (`closed` or `already-closed`) and URL. Refused when merged.                      |
| `pr_comments`            | Read only. Takes `repo` (`owner/repo`), `pr` and optional `exactBodies`; returns the review decision, every comment, review and inline thread, each with `updatedAt`, and a summary. Bodies are exact only with `exactBodies`.                |
| `pr_diff`                | Read only. Takes `repo` (`owner/repo`), `pr` and optional `mode` (`diff`, `name-only` or `stat`; default `diff`); returns the unified diff, each file's status letter and path, or its additions and deletions, and the outcome.              |
| `pr_edit`                | Takes `repo` (`owner/repo`), `pr` and at least one of `title`, `body`, `priority` and `assignSelf`; sets them in one write, keeping other labels and assignees; returns the outcome, URL and any old and new priority.                        |
| `pr_hand_off`            | Takes `repo` (`owner/repo`), `pr`, `title`, `description` and optional `record`; once the pull request is open, unheld and passing, sets them, Human Review, ready, the review record and auto-merge; returns the outcome and steps made.     |
| `pr_list`                | Read only. Takes `repo` and optional `issue`, `head`, `labels`, `closingIssues`; returns the open pull requests matching every filter, by number, with `labels` (and `closingIssues` if asked). Fails rather than return partial or over 1000.|
| `pr_ready_at`            | Read only. Takes `repo` (`owner/repo`) and `pr`; reads its whole timeline and returns `readyAt` (UTC) and `source`: `ready-for-review` for the last time it was marked ready, else `created` with its creation time. Ignores later drafts.    |
| `pr_reopen`              | Takes `repo` (`owner/repo`), `pr` and optional `comment`, posted first; reopens a closed pull request and returns the outcome (`open` or `already-open`) and URL. Refused for a merged pull request.                                          |
| `pr_reply`               | Takes `repo` (`owner/repo`), `pr`, `commentId` of an inline review comment and `body`; replies in its thread; returns the outcome (`Posted`, `Refused`, `NotFound`, `NotPosted` or `MayHaveBeenPosted`), a message, the reply's id and URL.   |
| `pr_review_comment`      | Takes `repo` (`owner/repo`), `pr`, `path`, `line`, `body` and optional `startLine` and `side` (`RIGHT` or `LEFT`); comments on those diff lines at the head commit; returns the outcome, a message, the comment's id and URL.                 |
| `pr_search`              | Read only. Takes `repo` (`owner/repo`), `text` and optional `state` (`open`, `closed`, `merged` or `all`; default `open`) and `limit` (1 to 100, default 30); returns each match's number, state and title, and whether more matched.         |
| `pr_status`              | Read only. Takes `repo` (`owner/repo`) and `pr`; returns its metadata, changed file count, board status, ready-at, check verdict with failing and pending names, and comment activity. Fails, returning nothing, if any read fails.           |
| `pr_sync_labels`         | Takes `repo` (`owner/repo`), `pr` and optional `assignSelf`; adds the closing issues' labels, priority too, never Blocked or On Hold, each in the repo's spelling, skipping any it lacks; removes none. Returns outcome, added, skipped.      |
| `pr_view`                | Read only. Takes `repo` (`owner/repo`), `pr` and optional `fields`; returns them in canonical order, by default all but `files`, `body` (only on their own) and the opt-in fields named in the tool description. Fails on a partial read.     |
| `release_list`           | Read only. Takes `repo` (`owner/repo`); returns its non-draft releases, newest `publishedAt` first, each with `tag`, `isLatest`, `isPrerelease` and `name` (`-` when none), without the notes. More than 1000 is refused.                     |
| `release_view`           | Read only. Takes `repo` (`owner/repo`); returns its latest release's `tag`, `publishedAt` (ISO-8601), `url` and `name`, empty when it has none, without the notes. No latest release is not a missing repository.                             |
| `repo_advisories`        | Read only. Takes `repo` (`owner/repo`); returns its own security advisories, newest first, each with `ghsaId`, `severity` (`-` when none), `state` and `summary`. A missing repository is its own error. More than 1000 is refused.           |
| `repo_collaborators`     | Read only. Takes `repo` (`owner/repo`); returns its collaborators sorted by `login`, each with `login` and `role` (`-` when none). Needs push access, so a 403 is an error, never an empty list. More than 1000 is refused.                   |
| `repo_dependabot_alerts` | Read only. Takes `repo` (`owner/repo`) and optional `state` (`open`, `fixed`, `dismissed`, `auto_dismissed` or `all`; default `open`), `severity` and `limit` (1 to 100, default 30); returns each alert's severity, state, package and fix.  |
| `repo_file`              | Read only. Takes `repo` (`owner/repo`), `path` and optional `ref`; returns a file's exact text and size, or a directory's entries sorted by name; refuses binary, over 1 MiB or 1000+ entries. Not found never names the path or ref.         |
| `repo_list`              | Read only. Takes `owner` and optional `includeArchived` (default false); returns the repositories the owner owns, sorted by name, each with visibility, archived flag and default branch. Fails rather than list over 1000.                   |
| `repo_search`            | Read only. Takes `owner`, `text` and optional `includeArchived` (default false) and `limit` (1 to 100, default 30); returns each matching repository's name, visibility, status and default branch, sorted by name, and whether more matched. |
| `repo_view`              | Read only. Takes `repo` (`owner/repo`); returns its default branch (empty for an empty repository), visibility, whether merge commits, squash and rebase merges are allowed (null when GitHub did not say, never false) and description.      |
| `run_list`               | Read only. Takes `repo` (`owner/repo`), exactly one of `branch` or `sha`, and optional `workflow` and `limit` (1 to 50, default 20); returns the runs newest first, each with id, workflow, status, conclusion, SHA, event and time.          |
| `run_log`                | Read only. Takes `repo` (`owner/repo`), `run`, optional `job`, `lines` (1 to 2000, default 200) or a search (below); returns `Log` with the sanitised tail or matches of each failed step, `Pending` or `NotFound`. Fails over 256 KiB.       |
| `run_rerun`              | Takes `repo` (`owner/repo`) and `run`; reruns the failed jobs of an open pull request head's run, or the whole run when a required check never ran; returns `Success` with the attempt, `Pending`, `Refused` with the rule or `NotFound`.     |
| `run_view`               | Read only. Takes `repo` (`owner/repo`) and `run`; returns the run's id, workflow, status, conclusion, branch, SHA, event, attempt and creation time, and the jobs of its latest attempt sorted by id.                                         |
| `tag_view`               | Read only. Takes `repo` (`owner/repo`) and `tag`; returns `sha`, the commit the tag points to (never the tag object), and `type` (`lightweight` or `annotated`), following one annotated tag. A missing tag is not a missing repository.      |
| `workflow_board_items`   | Read only. Takes `repo` (`owner/repo`); reads every page of the Workflow board and returns each issue and pull request on it with its `kind`, `repository`, `number` and `state` (left out when unset), and the `total`. Drafts are left out. |
| `workflow_list`          | Read only. Takes `repo` (`owner/repo`); returns the repository's GitHub Actions workflows, disabled ones included, sorted by path, each with name, path and state. Fails rather than list over 1000.                                          |
| `workflow_run`           | Takes `repo` (`owner/repo`), `workflow`, optional `ref` and `inputs` (`key=value`); starts a workflow the allowlist (below) permits; returns `Success` with the run, `Refused`, `NotFound` or `InvalidInput`.                                 |
| `workflow_status_check`  | Read only. Takes `repo` (`owner/repo`) and exactly one of `pr` or `issue`; returns the outcome (`Found`, `Unset` or `NotOnBoard`), the Workflow board status when one is set, and a message when the item is not on the board.                |
| `workflow_status_set`    | Takes `repo` (`owner/repo`), `status` (a workflow state, in any case) and exactly one of `pr` or `issue`; moves the item to that status on the Workflow board, adding it to the board first, and returns its URL and the status set.          |

Given `grep`, a .NET regular expression of 1 to 256 characters, `run_log` searches each failed step's whole log instead of reading its tail, and ignores `lines`. It keeps at most `maxMatches` (1 to 200, default 20) matching lines across the run, each with `context` (0 to 20, default 0) lines before and after it, starting from line `fromLine` (default 1). A matching line is shown as `| N: text` and a context line as `| N- text`, where `N` is its line number in the step's log, with `--` between lines that are not next to each other. Each line is sanitised before it is matched, so a redacted token or a masked secret can never match. `Matches` counts every matching line, kept or not, and `Truncated` is true when more lines matched than were kept.

### Dispatch allowlist

`workflow_run` only starts a workflow that the target repository lists in `.github/cfwf-dispatch-allow`, read from its default branch. A repository without the file refuses every call.
Each line names a workflow file, then the refs it may run on, separated by spaces; `*` permits any ref and the default branch is always permitted. A `#` starts a comment, several lines for the same workflow are combined, and a line that cannot be read, or that contains a tab, is ignored.

```text
# Runs on the default branch only
deploy.yml
# Runs on the default branch, release and hotfix
publish.yml release hotfix
# Runs on any branch
build.yml *
```

The workflow must also declare `workflow_dispatch`, and every input given must be one it declares, with no value that would leak a secret, token or private repository name.

### Listening ports

| Port   | Protocol | Binding        | When                                                        |
|--------|----------|----------------|-------------------------------------------------------------|
| `8080` | HTTP/1.1 | Loopback only  | Always; used by the container health check.                 |
| `8081` | HTTPS    | All interfaces | Only when a `server.pfx` certificate is next to the binary. |

### Health check mode

Running the executable as `Credfeto.Workflow.Mcp.Server --health-check http://127.0.0.1:8080/ping` calls the URL and exits with `0` when it succeeds and `1` when it does not, so a container `HEALTHCHECK` can use the server binary itself.

### Running in a container

The [Dockerfile][dockerfile] packages the published native executable into a minimal, non-root .NET `runtime-deps` chiseled image.
It does not compile the source: publish the server first, copy the `Dockerfile` into the publish output folder and use that folder as the build context.
Replace `docker` with `podman` if that is the container runner installed; with `podman build`, add `--format docker` or the image loses its `HEALTHCHECK`.

```bash
cd src/Credfeto.Workflow.Mcp.Server
dotnet publish --configuration Release -r linux-x64 --self-contained --output ../../server-dist/linux-x64
cd ../..
cp Dockerfile server-dist/linux-x64/
docker build --tag credfeto/workflow-mcp:local server-dist/linux-x64
```

Use `-r linux-arm64` (and a `server-dist/linux-arm64` folder) to build for 64-bit Arm.

The server only accepts connections from outside the container over HTTPS on port `8081`, and only when a certificate is mounted at `/usr/src/app/server.pfx`.
Without it, the server still starts and the health check still passes (plain HTTP on `8080` is loopback-only), but `/mcp` cannot be reached from outside the container.
The certificate must be a PKCS#12 file with no password, readable by the image's `app` user (UID `1654`).

```bash
docker run --detach --name workflow-mcp \
  --publish 8081:8081/tcp \
  --mount type=bind,source=/path/to/server.pfx,target=/usr/src/app/server.pfx,readonly \
  --env GitHub__ApiBaseUrl=https://api.github.com/ \
  --read-only --tmpfs /tmp \
  --cap-drop ALL \
  --security-opt no-new-privileges \
  credfeto/workflow-mcp:local
```

`GitHub__ApiBaseUrl` is optional and defaults to `https://api.github.com/`; any other [configuration][configuration] setting can be passed the same way.
The image does not include `libmsquic`, so the container serves HTTP/1.1 and HTTP/2 only; HTTP/3 is not available.
Check the container's health and that the server answers over HTTPS (add `--insecure` to `curl` for a self-signed certificate):

```bash
docker inspect --format '{{.State.Health.Status}}' workflow-mcp
curl https://<host>:8081/ping
```

### Parameter rules

Tool parameters are checked before any GitHub call is made.
A value that breaks a rule is rejected with a message that names the parameter and the rule, never the value itself.

| Parameter         | Rule                                                                                                                                                   |
|-------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------|
| Repository        | `owner/repo`: exactly one `/`; each part uses only letters, digits, `_`, `.` and `-`, and is not `.` or `..`.                                          |
| Number            | Digits `0-9` only; leading zeros are removed, and zero is allowed.                                                                                     |
| Issue/PR number   | Digits `0-9` only, greater than zero; leading zeros are removed.                                                                                       |
| Comment ID        | Digits `0-9` only, greater than zero, at most 19 digits once leading zeros are removed.                                                                |
| Commit SHA        | Exactly 40 hexadecimal characters; returned in lower case.                                                                                             |
| Branch            | A valid git ref name (see below) that does not start with `-` and is not `HEAD`.                                                                       |
| Tag               | A valid git ref name (see below).                                                                                                                      |
| Status            | Any text without control characters (`U+0000`-`U+001F`, `U+007F`-`U+009F`).                                                                            |
| Label             | Not empty or whitespace, no `,` or control characters, at most 50 characters counted as Unicode scalar values (an emoji counts once).                  |
| Label colour      | Exactly 6 hexadecimal characters without `#`; returned in lower case.                                                                                  |
| Label description | Not empty or whitespace, no control characters, at most 100 characters counted as Unicode scalar values (an emoji counts once).                        |
| Login             | Letters, digits, `_` and `-` only, optionally followed by `[bot]`.                                                                                     |
| Owner             | Letters, digits and `-` only, and does not start with `-`.                                                                                             |

A valid git ref name is not empty or `@`; does not contain `..`, `@{`, `//`, a space, `~`, `^`, `:`, `?`, `*`, `[`, `\` or a control character; does not start with `/` or end with `/` or `.`; and has no `/`-separated component that starts with `.` or ends with `.lock`.

When a branch name is placed in a GitHub API path, every UTF-8 byte other than `A-Z`, `a-z`, `0-9`, `.`, `_`, `~`, `-` and `/` is percent-encoded, so `feature/a+b` becomes `feature/a%2Bb`.

### Comment body rules

An agent's comment that contains one of the two accepted plan-approval keywords could be read as a human approving a plan, so comment bodies are checked for them.

- Either keyword is matched in any case, as a whole word.
- A letter, digit or underscore directly on either side makes it part of a longer word, which passes.
- Underscores around the keyword with only a word boundary beyond them are Markdown emphasis, which renders as the bare keyword, so that is still a match.
- A Markdown quote line (a `>` after at most three leading spaces) is ignored, so a human's own words can be quoted. A `>` after four or more spaces, or after a tab, is Markdown code rather than a quote, so a keyword on that line still counts.
- A refusal reports the rule name and the 1-based line number of the first matching line, never the matched text.

### Title rules

Issue and pull request titles are checked before any GitHub call is made, matching cfwf's rules.

Every title must:

- contain no control characters (`U+0000`-`U+001F`, `U+007F`-`U+009F`), so a title is always one line;
- contain at least one visible character: a title made only of whitespace (including Unicode spaces such as `U+3000`) or invisible characters is refused;
- be at most 256 characters, counted as Unicode scalar values (an emoji counts once). Invisible characters still count towards the limit.

Invisible characters are code points that render as nothing or as blank space: `U+00A0`, `U+00AD`, `U+034F`, `U+061C`, `U+115F`-`U+1160`, `U+17B4`-`U+17B5`, `U+180B`-`U+180F`, `U+2007`, `U+200B`-`U+200F`, `U+202A`-`U+202F`, `U+2060`-`U+206F`, `U+2800`, `U+3164`, `U+FE00`-`U+FE0F`, `U+FEFF`, `U+FFA0`, `U+FFF0`-`U+FFF8`, `U+1BCA0`-`U+1BCA3`, `U+1D173`-`U+1D17A` and `U+E0000`-`U+E0FFF`.

On top of that:

- **Issue titles** in repositories owned by `credfeto` or `funfair-tech` (in any case) must be `prefix: text` or `prefix(name): text`. The prefix is lower-case letters and digits, starting with a letter, in `-`-separated parts (for example `oneshot` or `tool-1`). The optional name is in brackets, has no `(`, `)` or `:`, and does not start or end with whitespace. One or more spaces follow the `:`, and the text after them must contain a visible character. Issue titles for other owners get only the common checks.
- **Pull request titles** must be Conventional Commits titles, `<type>[(<scope>)][!]: <description>`: a lower-case type, an optional non-empty scope in brackets without nested brackets, an optional `!`, then a `:` followed by a space and a description that starts with a non-whitespace character and contains a visible character.

The same visible-text, control-character and length checks apply to labels, with a 50 character limit and no `,`.

A refused title is reported with a fixed message that never echoes the title:

- `invalid value for --title: it must not be blank or longer than 256 characters`
- `invalid value for --title: it must be "prefix: text" or "prefix(name): text" with a lower-case prefix, for example "oneshot: ..." or "tool(cfwf): ..."`
- `invalid value for --title: it must be a Conventional Commits title, <type>[(<scope>)][!]: <description>`

### Configuration

Settings are read, in order, from `appsettings.json`, an optional `appsettings-local.json` beside it, environment variables (use `__` as the section separator, for example `Logging__LogLevel__Default`), and command-line arguments (use `:` as the section separator, for example `--GitHub:ApiBaseUrl=https://ghe.example.com/api/v3/`).
Later sources override earlier ones.

| Setting                                 | Type   | Default                   | Description                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |
|-----------------------------------------|--------|---------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `Logging:LogLevel:Default`              | string | `Information`             | Minimum log level for all categories.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| `Logging:LogLevel:Microsoft.AspNetCore` | string | `Warning`                 | Minimum log level for ASP.NET Core categories.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                          |
| `GitHub:ApiBaseUrl`                     | URL    | `https://api.github.com/` | GitHub API base URL (environment variable `GitHub__ApiBaseUrl`, command-line argument `--GitHub:ApiBaseUrl=<url>`). Must be an absolute `https` URL (plain `http` only for a loopback host such as `localhost`, `127.0.0.1` or `::1`, because every request carries the caller's token) whose path ends in `/` (a host-only URL such as `https://api.github.com` qualifies), has no query or fragment, and has no segment that, once percent-decoded until stable, split on `/` or `\`, cut at the first `;`, `?` or `#` and trimmed of whitespace and control characters, is `.` or `..`, nor any escape that cannot be decoded (such as `%c0%ae`); names such as `...` or `.github` are allowed; the server will not start otherwise. |
| `GitHub:ApiVersion`                     | string | `2026-03-10`              | GitHub REST API version sent as the `X-GitHub-Api-Version` header on every request (environment variable `GitHub__ApiVersion`, command-line argument `--GitHub:ApiVersion=<version>`). Set it to an empty value to send no version header, for GitHub Enterprise Server releases that reject it. Must only contain printable ASCII characters (no control characters or line breaks); the server will not start otherwise.                                                                                                                                                                                                                                                                                                              |
| `GitHub:UpstreamBasePath`               | string | (none)                    | Base path the upstream GitHub API serves under, when it differs from the path of `GitHub:ApiBaseUrl` because a proxy mounts it elsewhere (environment variable `GitHub__UpstreamBasePath`). A next page link on another host has this path, rather than the configured one, removed before it is mapped under `GitHub:ApiBaseUrl` (a link not under it loses only its leading `/`), and GraphQL is sent to the upstream GraphQL path mounted under the proxy path: `https://proxy/ghe/` with `/api/v3/` posts to `https://proxy/ghe/api/graphql`. Must start and end in `/`, be already percent-encoded, have no query or fragment and follow the `GitHub:ApiBaseUrl` segment rules; the server will not start otherwise.               |
| `GitHub:Board:Token`                    | string | (none)                    | Fine-grained token with Projects read and write on the Workflow board's owner (environment variable `GitHub__Board__Token`); set it from an environment variable or secret store, never in a committed settings file. Used only when `issue_create` is refused the Workflow board with the caller's token: it reads only the repository's linked Workflow project, its Status field and the Not Started option, and only adds the new issue as Not Started. It is never logged. Must be printable ASCII with no spaces, control characters or line breaks, and not blank; the server will not start otherwise.                                                                                                                          |
| `Workflow:PrivateOwners`                | string | (none)                    | Space-separated GitHub owner names whose repositories are private (environment variable `Workflow__PrivateOwners`). Text written to a public repository is refused when it names a repository of one of these owners as a bare `owner/repo`, without its visibility being looked up; links and `owner/repo#N` or `owner/repo@sha` references to any other owner are refused when that repository is not public. Each name may only contain letters, digits and `-` and must not start with `-`; the server will not start otherwise.                                                                                                                                                                                                    |

## Documentation

See the [docs][docs] folder, including the `.http` request files for the [HTTP API][api-http], the [MCP endpoint][mcp-http] and the [GitHub API calls][github-http] the server makes.

## Changelog

View [changelog][changelog].

## Contributing

See [contributing guidelines][contributing].

## Security

See [security policy][security].

## Licence

This project is licensed under the [MIT Licence][licence].

[api-http]: docs/api.http
[changelog]: CHANGELOG.md
[configuration]: #configuration
[contributing]: CONTRIBUTING.md
[dockerfile]: Dockerfile
[docs]: docs/
[github-http]: docs/github.http
[licence]: LICENSE
[mcp-http]: docs/mcp.http
[pre-release-img]: https://github.com/credfeto/credfeto-workflow-mcp/actions/workflows/build-and-publish-pre-release.yml/badge.svg
[pre-release]: https://github.com/credfeto/credfeto-workflow-mcp/actions/workflows/build-and-publish-pre-release.yml
[release-img]: https://github.com/credfeto/credfeto-workflow-mcp/actions/workflows/build-and-publish-release.yml/badge.svg
[release]: https://github.com/credfeto/credfeto-workflow-mcp/actions/workflows/build-and-publish-release.yml
[security]: SECURITY.md
