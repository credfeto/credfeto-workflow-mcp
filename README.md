# credfeto-workflow-mcp

An MCP (Model Context Protocol) server that gives AI agents the GitHub workflow operations currently provided by the `cfwf` command-line tool.

| Branch  | Status                                                |
|---------|-------------------------------------------------------|
| main    | [![Build: Pre-Release][pre-release-img]][pre-release] |
| release | [![Build: Release][release-img]][release]             |

## Overview

credfeto-workflow-mcp is an ASP.NET Core server, published as a native AOT executable, that exposes workflow tools over the MCP Streamable HTTP transport so agents can call them directly instead of shelling out to `cfwf`.
The MCP transport is stateless, so every request stands alone.
The server currently provides the `ping` tool and a matching HTTP health check; the workflow tools that replace `cfwf` follow in later releases.

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

| Tool   | Description                                                  |
|--------|--------------------------------------------------------------|
| `ping` | Returns `{"value":"pong"}`, the same payload as `GET /ping`. |

### Listening ports

| Port   | Protocol | Binding        | When                                                        |
|--------|----------|----------------|-------------------------------------------------------------|
| `8080` | HTTP/1.1 | Loopback only  | Always; used by the container health check.                 |
| `8081` | HTTPS    | All interfaces | Only when a `server.pfx` certificate is next to the binary. |

### Health check mode

Running the executable as `Credfeto.Workflow.Mcp.Server --health-check http://127.0.0.1:8080/ping` calls the URL and exits with `0` when it succeeds and `1` when it does not, so a container `HEALTHCHECK` can use the server binary itself.

### Configuration

Settings are read, in order, from `appsettings.json`, an optional `appsettings-local.json` beside it, and environment variables (use `__` as the section separator, for example `Logging__LogLevel__Default`).
Later sources override earlier ones.

| Setting                                 | Type   | Default                   | Description                                                                                                                                                    |
|-----------------------------------------|--------|---------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `Logging:LogLevel:Default`              | string | `Information`             | Minimum log level for all categories.                                                                                                                          |
| `Logging:LogLevel:Microsoft.AspNetCore` | string | `Warning`                 | Minimum log level for ASP.NET Core categories.                                                                                                                 |
| `GitHub:ApiBaseUrl`                     | URL    | `https://api.github.com/` | GitHub API base URL (environment variable `GitHub__ApiBaseUrl`). Must be an absolute `http` or `https` URL ending in `/`; the server will not start otherwise. |

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
[contributing]: CONTRIBUTING.md
[docs]: docs/
[github-http]: docs/github.http
[licence]: LICENSE
[mcp-http]: docs/mcp.http
[pre-release-img]: https://github.com/credfeto/credfeto-workflow-mcp/actions/workflows/build-and-publish-pre-release.yml/badge.svg
[pre-release]: https://github.com/credfeto/credfeto-workflow-mcp/actions/workflows/build-and-publish-pre-release.yml
[release-img]: https://github.com/credfeto/credfeto-workflow-mcp/actions/workflows/build-and-publish-release.yml/badge.svg
[release]: https://github.com/credfeto/credfeto-workflow-mcp/actions/workflows/build-and-publish-release.yml
[security]: SECURITY.md
