# slogs

`slogs` is a Korean developer blogging service focused on fast reading, Markdown writing, author discovery, and personal knowledge workflows.

Korean documentation: [README.md](README.md)

## About

GitHub repository description:

> Korean developer blogging service with Markdown writing, personal Slogger homes, tag and series discovery, social reading flows, and an Agent-ready LLM Wiki.

Suggested topics:

`blazor`, `dotnet`, `markdown`, `blog`, `postgresql`, `pgvector`, `llm-wiki`, `mcp`, `korean`

## Features

- Post discovery through recent, trending, recommended, and followed-author feeds
- Markdown writing and editing with live preview, image insertion, tags, series, drafts, and published revisions
- Slogger personal home pages with public profiles, post archives, followers, and following
- Reading interactions including comments, replies, likes, bookmarks, and follows
- Authenticated personal pages for posts, bookmarks, likes, settings, and LLM Wiki
- Slogs MCP tools for Agent-driven post publishing, image uploads, post deletion, and LLM Wiki memory workflows
- LLM Wiki search and recall backed by PostgreSQL `pgvector` and a server-local EmbeddingGemma model

## Tech Stack

- .NET 11 Preview, C# preview
- ASP.NET Core, Blazor `InteractiveAuto`
- Entity Framework Core, Npgsql
- PostgreSQL 16, `pgvector`
- Vditor for browser-side Markdown editing and preview
- Ollama-hosted EmbeddingGemma for local LLM Wiki embeddings
- Podman for local PostgreSQL and EmbeddingGemma containers

## Repository Layout

- `src/Slogs`: ASP.NET Core server, REST APIs, EF Core data layer, authentication, MCP tools
- `src/Slogs.Client`: Blazor client UI and routed pages
- `src/Slogs.Shared`: Shared contracts, API client, auth state, Markdown and SEO helpers
- `src/Slogs.NativeAotProbe`: Experimental NativeAOT publish probe
- `scripts`: Local service, deployment, and Slogs MCP prompt sync scripts
- `infra/postgres`: Podman compose services for PostgreSQL and EmbeddingGemma
- `artifacts`: Local verification output and generated runtime artifacts. This directory is intentionally not tracked.

## Local Development

Prerequisites:

- PowerShell
- .NET 11 Preview SDK matching `global.json`
- Podman
- NVIDIA GPU + CDI support when running the LLM Wiki embedding service

Start PostgreSQL:

```powershell
scripts/Start-Postgres.ps1
```

Run the app:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project src/Slogs/Slogs.csproj --launch-profile https
```

The HTTPS launch profile uses:

- `https://localhost:5000`
- `http://localhost:5001`

Start EmbeddingGemma when testing LLM Wiki semantic search or recall:

```powershell
scripts/Start-EmbeddingGemma.ps1
```

The development database connection is configured in `src/Slogs/appsettings.Development.json`.

## Build And Verification

Build the solution:

```powershell
dotnet build Slogs.slnx -warnaserror
```

Useful smoke-check routes:

- Public routes: `/`, `/recent`, `/trending`, `/recommended`, `/tag`, `/series`
- Auth routes: `/login`, `/register`, `/me`, `/write`
- LLM Wiki route: `/me/llm-wiki`

## Deployment

Routine deployments should not enable WebAssembly AOT by default:

```powershell
scripts/Deploy-Slogs.ps1
```

Use WebAssembly AOT only for an explicit AOT deployment:

```powershell
scripts/Deploy-Slogs.ps1 -WasmAot
```

## License

This project is licensed under the Apache License 2.0. See [LICENSE](LICENSE).
