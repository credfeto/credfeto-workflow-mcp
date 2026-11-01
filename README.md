# credfeto-workflow-mcp

An MCP (Model Context Protocol) server that gives AI agents the GitHub workflow operations currently provided by the `cfwf` command-line tool.

| Branch  | Status                                                |
|---------|-------------------------------------------------------|
| main    | [![Build: Pre-Release][pre-release-img]][pre-release] |
| release | [![Build: Release][release-img]][release]             |

## Overview

credfeto-workflow-mcp is an ASP.NET Core server, published as a native AOT executable, that exposes workflow tools over the MCP Streamable HTTP transport so agents can call them directly instead of shelling out to `cfwf`.
The MCP transport is stateless, so every request stands alone.
The server currently provides the `ping`, `pr_comments` and `branch_required_checks` tools and a matching HTTP health check; the other workflow tools that replace `cfwf` follow in later releases.

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

### MCP tools

| Tool                     | Description                                                                                                                                                                                                                                   |
|--------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `branch_required_checks` | Read only. Takes `repo` (`owner/repo`) and `branch`; returns the repository, the branch and the names of the status checks it requires before a merge, from branch protection and rulesets combined. Fails rather than return a partial list. |
| `ping`                   | Returns `{"value":"pong"}`, the same payload as `GET /ping`.                                                                                                                                                                                  |
| `pr_comments`            | Read only. Takes `repo` (`owner/repo`) and `pr`; returns the review decision, every top-level comment, review and inline review thread, and a summary of counts. Fails rather than return a partial list.                                     |

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

| Parameter        | Rule                                                                                                                                                   |
|------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------|
| Repository       | `owner/repo`: exactly one `/`; each part uses only letters, digits, `_`, `.` and `-`, and is not `.` or `..`.                                          |
| Number           | Digits `0-9` only; leading zeros are removed, and zero is allowed.                                                                                     |
| Issue/PR number  | Digits `0-9` only, greater than zero; leading zeros are removed.                                                                                       |
| Comment ID       | Digits `0-9` only, greater than zero, at most 19 digits once leading zeros are removed.                                                                |
| Commit SHA       | Exactly 40 hexadecimal characters; returned in lower case.                                                                                             |
| Branch           | A valid git ref name (see below) that does not start with `-` and is not `HEAD`.                                                                       |
| Tag              | A valid git ref name (see below).                                                                                                                      |
| Status           | Any text without control characters (`U+0000`-`U+001F`, `U+007F`-`U+009F`).                                                                            |
| Label            | Not empty or whitespace, no `,` or control characters, at most 50 characters counted as Unicode scalar values (an emoji counts once).                  |
| Login            | Letters, digits, `_` and `-` only, optionally followed by `[bot]`.                                                                                     |

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

| Setting                                 | Type   | Default                   | Description                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  |
|-----------------------------------------|--------|---------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `Logging:LogLevel:Default`              | string | `Information`             | Minimum log level for all categories.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| `Logging:LogLevel:Microsoft.AspNetCore` | string | `Warning`                 | Minimum log level for ASP.NET Core categories.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| `GitHub:ApiBaseUrl`                     | URL    | `https://api.github.com/` | GitHub API base URL (environment variable `GitHub__ApiBaseUrl`, command-line argument `--GitHub:ApiBaseUrl=<url>`). Must be an absolute `https` URL (plain `http` only for a loopback host such as `localhost`, `127.0.0.1` or `::1`, because every request carries the caller's token) whose path ends in `/` (a host-only URL such as `https://api.github.com` qualifies) and has no segment that, once percent-decoded until stable, split on `/` or `\`, cut at the first `;`, `?` or `#` and trimmed of whitespace and control characters, is `.` or `..`, nor any escape that cannot be decoded (such as `%c0%ae`); names such as `...` or `.github` are allowed; the server will not start otherwise. |

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
