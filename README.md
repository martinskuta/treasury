# Treasury

A self-hosted family finance tracker. See [GLOSSARY.md](GLOSSARY.md) for the domain language and
[docs/adr/](docs/adr/) for architecture decisions.

## Repository layout

| Path | What |
|---|---|
| `src/Treasury.AppHost` | Aspire AppHost: describes the API, the React app and PostgreSQL; generates the docker-compose deployment |
| `src/Treasury.ServiceDefaults` | Shared Aspire defaults: health checks, OpenTelemetry, HTTP resilience, service discovery |
| `src/Treasury.Api` | ASP.NET Core Web API (.NET 11); in production it also serves the built React app |
| `web/` | React + TypeScript frontend (Vite) |
| `tests/Treasury.Api.Tests` | TUnit API tests against a real PostgreSQL (Testcontainers) |

## Prerequisites

- .NET SDK 11.0.100 (pinned in `global.json`)
- Node.js 24+ and npm
- Docker (for PostgreSQL locally and for the tests)
- Aspire CLI **17.0.0** or newer (`aspire --version`). Install or upgrade with
  `irm https://aspire.dev/install.ps1 | iex` (Windows), `curl -sSL https://aspire.dev/install.sh | bash`
  (Linux/macOS) or `winget upgrade Microsoft.Aspire`.

## Run locally

```bash
aspire run
```

From the repository root (`aspire.config.json` points at the AppHost). This starts PostgreSQL, the API and the
Vite dev server and opens the Aspire dashboard; the `web` resource's URL is the app. The React app calls
`/api/...`, which Vite proxies to the API.

The PostgreSQL container and its `treasury-pgdata` volume are persistent and survive `aspire run` restarts.
To start from an empty database: `docker rm -f <postgres container>` and `docker volume rm treasury-pgdata`.

## Run tests and checks

```bash
dotnet build
dotnet test                 # Docker must be running

cd web
npm ci
npm run typecheck
npm run lint
npm run build
```

## Docker Compose deployment

The compose files are generated from the AppHost (ADR-0004), never edited by hand:

```bash
aspire publish -o deploy    # writes deploy/docker-compose.yaml and deploy/.env
aspire deploy -o deploy     # also builds the image and runs docker compose up
```

The app is served on `http://localhost:${TREASURY_HTTP_PORT:-8080}` (API and React app in one container) and the
Aspire dashboard on port 18888. `deploy/` holds secrets (`.env.Production`) and is git-ignored.
