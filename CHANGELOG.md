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
### Fixed
### Changed
### Deprecated
### Removed
### Deployment Changes

<!--
Releases that have at least been deployed to staging, BUT NOT necessarily released to live.  Changes should be moved from [Unreleased] into here as they are merged into the appropriate release branch
-->

## [0.0.0] - Project created