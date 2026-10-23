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

### Configuration

Settings are read, in order, from `appsettings.json`, an optional `appsettings-local.json` beside it, environment variables (use `__` as the section separator, for example `Logging__LogLevel__Default`), and command-line arguments (use `:` as the section separator, for example `--GitHub:ApiBaseUrl=https://ghe.example.com/api/v3/`).
Later sources override earlier ones.

| Setting                                 | Type   | Default                   | Description                                                                                                                                                                                                                                                                             |
|-----------------------------------------|--------|---------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `Logging:LogLevel:Default`              | string | `Information`             | Minimum log level for all categories.                                                                                                                                                                                                                                                   |
| `Logging:LogLevel:Microsoft.AspNetCore` | string | `Warning`                 | Minimum log level for ASP.NET Core categories.                                                                                                                                                                                                                                          |
| `GitHub:ApiBaseUrl`                     | URL    | `https://api.github.com/` | GitHub API base URL (environment variable `GitHub__ApiBaseUrl`, command-line argument `--GitHub:ApiBaseUrl=<url>`). Must be an absolute `http` or `https` URL whose path ends in `/` (a host-only URL such as `https://api.github.com` qualifies); the server will not start otherwise. |

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
