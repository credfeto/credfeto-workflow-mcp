# Build context is the published output folder (dotnet publish -r <rid> --self-contained), not the repository root
FROM mcr.microsoft.com/dotnet/runtime-deps:11.0.0-rc.2-resolute-chiseled@sha256:47ad5de0d321860bbef0ed32d652452b7d59a0de76fe52c77aebfcb112add819

WORKDIR /usr/src/app

# Bundle App and basic config
COPY Credfeto.Workflow.Mcp.Server .
COPY appsettings.json .

USER $APP_UID

# HTTPS only listens when server.pfx is mounted at /usr/src/app/server.pfx; plain HTTP on 8080 is loopback-only
EXPOSE 8081/tcp
ENTRYPOINT [ "/usr/src/app/Credfeto.Workflow.Mcp.Server" ]

# Perform a healthcheck.  note that ECS ignores this, so this is for local development
HEALTHCHECK --interval=5s --timeout=2s --retries=3 --start-period=5s CMD [ "/usr/src/app/Credfeto.Workflow.Mcp.Server", "--health-check", "http://127.0.0.1:8080/ping" ]
