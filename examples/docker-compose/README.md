# Self-hosting Treasury with Docker Compose

Runs Treasury from the published image `ghcr.io/martinskuta/treasury`, without cloning the repository or installing
.NET, Node.js or the Aspire CLI. You need Docker with the Compose plugin (`docker compose version`).

| File | What |
|---|---|
| `docker-compose.yaml` | The stack: `api` (Treasury, also serves the web app), `postgres` and `compose-dashboard` (Aspire dashboard: logs, traces, metrics) |
| `.env.example` | Template for the settings; copy it to `.env` |

## Start

1. Download `docker-compose.yaml` and `.env.example` into an empty folder and copy `.env.example` to `.env`.
2. In `.env`, set `ADMIN_PASSWORD` (upper- and lower-case letter, digit, symbol, at least 6 characters) and
   `POSTGRES_PASSWORD` (long and random).
3. On Linux, create the data folder and give it to the container user (uid 1654). Docker Desktop on Windows/macOS
   needs no `chown`.

   ```bash
   mkdir -p data
   sudo chown -R 1654:1654 data
   ```

4. Start the stack:

   ```bash
   docker compose up -d
   curl http://localhost:8080/api/health
   ```

5. Open `http://<server>:8080`, log in with `ADMIN_USERNAME` / `ADMIN_PASSWORD` and change the password right away
   (user name at the top right, **Change password**). The first Admin is only created while there are no Members;
   afterwards the two values in `.env` are ignored.

## Where the data lives

- Database: Docker volume `<folder name>_treasury-pgdata`. `docker compose down -v` deletes it; don't use `-v`.
- Ingest folders and Source files: `./data` (or `TREASURY_DATA_DIR`), mounted at `/data` in the container.

Back up both: see [docs/backup.md](../../docs/backup.md).

### Dashboard telemetry

By default the Aspire dashboard loses the API's logs, traces and metrics whenever its container restarts. To keep
them, switch the dashboard to the `Resume` or `Run` persistence mode: the comments on the `compose-dashboard`
service in `docker-compose.yaml` describe the three modes and hold the lines to uncomment. The telemetry then lives
in the Docker volume `<folder name>_treasury-dashboard-data`; it needs no backup.

## Update

```bash
docker compose pull
docker compose up -d
```

The API applies database migrations itself at startup. Make a backup first.

## More

[docs/deployment.md](../../docs/deployment.md) covers dropping statement files into Ingest folders (network share,
sync tools), the Aspire dashboard login token, network access and troubleshooting. It describes the other way to
deploy, building the image yourself with `aspire deploy`; with this example use plain `docker compose ...` in this
folder instead of its `dc` helper, and `.env` instead of `.env.Production`.

## Regenerating `docker-compose.yaml`

The services, variables and volumes in the file are generated from the Aspire AppHost (ADR-0004), not written by
hand. The only hand-written part is comments: the telemetry storage notes on `compose-dashboard` and the
commented-out `treasury-dashboard-data` volume at the end. After changing `src/Treasury.AppHost/AppHost.cs`,
regenerate the file from the repository root and put those comments back:

```bash
aspire publish -o deploy
cp deploy/docker-compose.yaml examples/docker-compose/docker-compose.yaml
git diff examples/docker-compose/docker-compose.yaml   # shows the comments that were dropped; restore them
```

A new variable in the generated `deploy/.env` also needs a line in `.env.example`.
