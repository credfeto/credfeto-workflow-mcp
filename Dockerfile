# Build context is the published output folder (dotnet publish -r <rid> --self-contained), not the repository root
FROM mcr.microsoft.com/dotnet/runtime-deps:11.0.0-rc.3-resolute-chiseled@sha256:e7f17490b7d668dbcba8671ed287e3e023bb385e8aa7851f3c803acbd3aff61e

WORKDIR /usr/src/app

# Bundle App and basic config
COPY Credfeto.Workflow.Mcp.Server .
COPY appsettings.json .

USER 1654

# HTTPS only listens when server.pfx is mounted at /usr/src/app/server.pfx; plain HTTP on 8080 is loopback-only
EXPOSE 8081/tcp
ENTRYPOINT [ "/usr/src/app/Credfeto.Workflow.Mcp.Server" ]

# Perform a healthcheck.  note that ECS ignores this, so this is for local development
HEALTHCHECK --interval=5s --timeout=2s --retries=3 --start-period=5s CMD [ "/usr/src/app/Credfeto.Workflow.Mcp.Server", "--health-check", "http://127.0.0.1:8080/ping" ]
