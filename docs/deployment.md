# Deploying Treasury at home

Treasury runs as a small Docker Compose stack generated from the Aspire AppHost (ADR-0004). This guide installs it
on one machine at home (a Linux server or NAS, or a Windows/macOS machine with Docker Desktop) and covers the first
login, dropping statement files into Ingest folders, settings and updates. Backups are in [backup.md](backup.md).

## What runs

| Compose service | What | Host port |
|---|---|---|
| `api` | The Treasury API, which also serves the React app | `${TREASURY_HTTP_PORT:-8080}` |
| `postgres` | PostgreSQL 18, data in the Docker volume `treasury-pgdata` | none (only reachable inside the stack) |
| `compose-dashboard` | Aspire dashboard: logs, traces and metrics of the API | `18888` |

| Storage | Where | Holds |
|---|---|---|
| Docker volume `<project>_treasury-pgdata` | managed by Docker | the database: Members, Accounts, Transactions, ... |
| Data folder, host folder `${TREASURY_DATA_DIR:-./data}` (relative to the compose file), mounted at `/data` | your disk | `ingest/<account-slug>/` (Ingest folders, with `processed/` and `failed/`) and `source-files/` (Treasury's copy of every imported Source file) |

The compose files are generated, never edited by hand. Changes to services, volumes, ports or environment
variables are made in `src/Treasury.AppHost/AppHost.cs`.

## 1. Prerequisites

The machine that runs `aspire deploy` builds the image and starts the stack, so the simplest setup is to do
everything on the home server itself:

- Docker Engine with the Compose plugin (`docker compose version`), or Docker Desktop
- Git
- .NET SDK 11.0.100 (pinned in `global.json`)
- Node.js 24+ and npm (the React app is built during deploy)
- Aspire CLI **17.0.0** or newer (`aspire --version`); see the README for install commands

```bash
git clone https://github.com/martinskuta/treasury.git ~/treasury
cd ~/treasury
```

Keep this clone in the same place: `aspire deploy` derives the Compose project name (and with it the database volume
name) from the AppHost's path. See [Updating](#updating-to-a-new-version).

## 2. Choose where the data folder lives

By default the data folder is `deploy/data` inside the clone. That works, but it sits next to source code where a
`git clean -xdf` would delete it, so on a server prefer a folder of its own. The API container runs as the non-root
user **uid 1654**, which must be able to read and write it. On Linux:

```bash
sudo mkdir -p /srv/treasury/data
sudo chown -R 1654:1654 /srv/treasury/data
```

Docker Desktop (Windows/macOS) maps permissions for you; no `chown` is needed there.

Host-specific settings are Compose variables. `aspire deploy` rewrites `deploy/.env.Production` on every run, so
don't put them there; keep them in a file of your own and load it into the shell before every `aspire deploy` and
`docker compose` command:

```bash
# ~/treasury.env (not inside the clone)
TREASURY_DATA_DIR=/srv/treasury/data   # data folder; default ./data next to the compose file
TREASURY_HTTP_PORT=8080                # host port of the app; default 8080
```

```bash
set -a; . ~/treasury.env; set +a
```

(Add that line to `~/.profile` to have it in every shell.)

## 3. Choose the parameters

| Parameter | Compose variable | What |
|---|---|---|
| `admin-username` | `ADMIN_USERNAME` | User name of the first Admin. Default `admin`. |
| `admin-password` | `ADMIN_PASSWORD` | Password of the first Admin: upper- and lower-case letter, digit, symbol, at least 6 characters. |
| `postgres-password` | `POSTGRES_PASSWORD` | Password of the `postgres` database user. Pick a long random one; you never type it in the app. |

The first Admin is created on the API's first start, and only while the database has no Members at all. After
that `ADMIN_USERNAME` / `ADMIN_PASSWORD` are ignored; further Members are added by the Admin in the app.

Give the parameters as environment variables (the names contain dashes, so use `env` rather than `export`):

```bash
env 'Parameters__admin-username=admin' \
    'Parameters__admin-password=<first password>' \
    'Parameters__postgres-password=<long random password>' \
    aspire deploy -o deploy --non-interactive
```

or run `aspire deploy -o deploy` without them and answer the prompts. On PowerShell:
`${env:Parameters__admin-password} = '...'`.

### Keeping the passwords safe

Aspire stores the parameter values **in plain text** in two places:

- `deploy/.env.Production` (read by `docker compose`), and
- `~/.aspire/deployments/<hash>/production.json` (so later `aspire deploy` runs don't ask again).

Anyone who can read those files can read the passwords. Mitigations:

1. **Treat the first Admin password as a one-time password.** Deploy with a temporary password, log in, and change
   it right away (click your user name at the top right, **Change password**). The value in the files then no longer
   opens anything: the API only uses it while there are no Members.
2. Restrict the files to your user: `chmod 700 deploy ~/.aspire/deployments`, and don't copy them anywhere public.
   `deploy/` is git-ignored.
3. `POSTGRES_PASSWORD` stays in the files (the API needs it). PostgreSQL has no host port, so it is only reachable
   from containers inside the stack; anyone with Docker access on the host can reach the database anyway.

## 4. Deploy

```bash
cd ~/treasury
set -a; . ~/treasury.env; set +a
env 'Parameters__admin-username=admin' 'Parameters__admin-password=<first password>' \
    'Parameters__postgres-password=<long random password>' aspire deploy -o deploy --non-interactive
```

This builds the React app and the API image (`api:aspire-deploy-<timestamp>`), writes `deploy/docker-compose.yaml`,
`deploy/.env` (empty template) and `deploy/.env.Production` (the values), and runs `docker compose up`. It ends with:

```
  api: http://localhost:8080
  compose-dashboard: http://localhost:18888
```

To only look at the generated files without building or starting anything: `aspire publish -o deploy`.

The API applies database migrations itself at startup. Check it is up:

```bash
curl http://localhost:8080/api/health
# {"status":"Healthy","checks":[{"name":"self","status":"Healthy"},{"name":"database","status":"Healthy"}]}
```

## 5. First login

Open `http://<server>:8080` and log in with the Admin user name and password from step 3. Then:

1. Change the password (user name at the top right, **Change password**).
2. Add the other Members (**Members**).
3. Create Accounts (**Accounts**). Creating an Account creates its Ingest folder `ingest/<account-slug>/` in the data
   folder; the slug is shown on the Account.

## Managing the stack

`aspire deploy` names the Compose project `aspire-compose-<8 hex digits>`. Find it with:

```bash
docker compose ls
# NAME                      STATUS       CONFIG FILES
# aspire-compose-c234f4ee   running(3)   /home/me/treasury/deploy/docker-compose.yaml
```

Always pass that project name and `.env.Production`. Without `-p`, Compose would use the folder name `deploy` and start
a second, empty stack; without `--env-file`, it reads the empty `deploy/.env`. A shell function helps:

```bash
cd ~/treasury/deploy
set -a; . ~/treasury.env; set +a
dc() { docker compose -p aspire-compose-c234f4ee --env-file .env.Production "$@"; }

dc ps
dc logs -f api
dc restart api
dc stop        # stop everything, keep data
dc up -d       # start again
```

All three services restart on their own after a reboot of the host (as long as Docker starts at boot). Don't run
`dc down -v`: `-v` deletes the database volume.

### Aspire dashboard

`http://<server>:18888` shows the API's logs, traces and metrics. It asks for a login token, printed in its log:

```bash
dc logs compose-dashboard | grep "login?t="
```

### Network access

The app speaks plain HTTP. That's fine on the home network; don't forward ports 8080 or 18888 to the internet. For
access from outside, use a VPN (WireGuard, Tailscale) or put a reverse proxy with HTTPS (Caddy, Traefik, nginx) in
front of port 8080; the API honours `X-Forwarded-*` headers (`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`).

## Dropping statement files into Ingest folders

Treasury checks every Ingest folder at startup and then every minute, imports each file it finds directly in
`ingest/<account-slug>/`, and moves it to `processed/` or to `failed/` (with a `<file>.error.txt` saying why). Files
are only picked up once they've stayed unchanged for 2 seconds; temporary names (`.part`, `.crdownload`, `.tmp`, ...)
and hidden files are skipped until renamed. So a share or sync tool can write straight into the folder.

The API needs to read the files and move them, so files must be readable by uid 1654 and the folders writable by it
(the Account folders are created by the API, owned by uid 1654).

### Network share (Samba)

Share the `ingest` folder and write to it as uid 1654:

```bash
sudo useradd --system --uid 1654 --no-create-home --shell /usr/sbin/nologin treasury
sudo smbpasswd -a <your user>     # if not yet a Samba user
```

```ini
# /etc/samba/smb.conf
[treasury-ingest]
   path = /srv/treasury/data/ingest
   valid users = <your user>
   read only = no
   force user = treasury
   force group = treasury
```

`sudo systemctl restart smbd`, then on a laptop open `\\<server>\treasury-ingest\<account-slug>\` (Windows) or
`smb://<server>/treasury-ingest` (macOS) and drop the bank's export there. Don't share the whole data folder:
`source-files/` must not be edited by hand.

On a Synology/QNAP NAS, create a shared folder for the data folder in the NAS UI and give uid 1654 read/write
permission on it (or create a local user with uid 1654 via SSH as above).

### Sync tool (Syncthing, Nextcloud, Resilio)

Sync a folder on your laptop to `ingest/<account-slug>/` on the server, one direction (send only from the laptop,
"receive only" on the server), so that the files Treasury moves to `processed/` don't come back. Run the sync
client on the server as uid 1654, or give it a group that uid 1654 is in and `chmod -R g+rwX` the Ingest folders.
Exclude `processed/` and `failed/` from the sync.

## Settings

| Setting | How to change it |
|---|---|
| `TREASURY_HTTP_PORT` | Compose variable (step 2), default `8080` |
| `TREASURY_DATA_DIR` | Compose variable (step 2), default `./data` next to the compose file |
| `ADMIN_USERNAME`, `ADMIN_PASSWORD`, `POSTGRES_PASSWORD` | Parameters (step 3) |
| `API_IMAGE`, `API_PORT` | Written by `aspire deploy`; don't change |
| `Ingest__ScanInterval` (default `00:01:00`), `Ingest__SettleTime` (default `00:00:02`) | API settings; to change them, add `.WithEnvironment(...)` on the `api` resource in the AppHost and redeploy |

## Updating to a new version

1. Make a backup ([backup.md](backup.md)).
2. Pull and redeploy from the **same clone**:

   ```bash
   cd ~/treasury
   git pull
   set -a; . ~/treasury.env; set +a
   aspire deploy -o deploy --non-interactive
   ```

   The parameters are taken from `~/.aspire/deployments/<hash>/production.json`, so they needn't be given again.
   The new API image starts against the existing database volume and data folder and applies any new database
   migrations at startup. Check `curl http://localhost:8080/api/health` and `dc logs api`.
3. Remove old images now and then: `docker image prune` and `docker rmi $(docker images 'api' -q --filter 'before=api:latest')`.

The Compose project name comes from the AppHost's path. If you move or re-clone the repository, `aspire deploy`
creates a new project with an **empty** database next to the old one (and fails on the busy port). Either keep
the clone where it is, or move the data across with a backup and restore ([backup.md](backup.md)), then remove
the old project with `docker compose -p <old project> down` (without `-v` until you're sure).

## Troubleshooting

- **No Admin can log in on a new installation**: the API log says
  `No Members exist and Admin:UserName / Admin:Password are not set`. Redeploy with the parameters. A password that
  breaks the password rules stops the API at startup with `Could not create the first Admin: ...` in `dc logs api`.
- **Files stay in the Ingest folder**: check that uid 1654 can read them (`ls -ln`), and `dc logs api`. `failed/`
  holds the files that couldn't be imported, each with an `.error.txt`.
- **`permission denied` on `/data` at startup** (Linux): the data folder isn't writable by uid 1654, see step 2.
