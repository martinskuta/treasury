# Backing up and restoring Treasury

A Treasury installation ([deployment.md](deployment.md)) keeps its state in two places, and a backup needs both:

| What | Where | Back up with |
|---|---|---|
| The database (Members and their passwords, Accounts, Transactions, Categories, ...) | Docker volume `<project>_treasury-pgdata` | `pg_dump` |
| The data folder: `ingest/` (Ingest folders with `processed/` and `failed/`) and `source-files/` (Treasury's copy of every imported Source file, which Transactions link to) | `${TREASURY_DATA_DIR}` (default `deploy/data` in the clone) | any file backup (`tar`, restic, borg, NAS snapshot) |

Don't back up the database by copying the Docker volume while PostgreSQL is running; use `pg_dump`.

Not needed for a restore: the image (rebuilt by `aspire deploy`), `deploy/` and the Aspire deployment state (their
passwords are only used to create a new installation; see [Restoring](#restoring)). Keep your `~/treasury.env`.

The commands below assume you're in `~/treasury/deploy` with the `dc` function and `~/treasury.env` from
[deployment.md](deployment.md#managing-the-stack) (`docker compose ls` shows the project name).

## Making a backup

```bash
# 1. The database, in pg_dump's custom format (compressed, restored with pg_restore).
#    PostgreSQL requires the password even inside the container; it's in the container's environment.
dc exec -T postgres sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" pg_dump -U postgres -Fc treasurydb' \
  > /backup/treasury/treasury-$(date +%F).dump

# 2. The data folder, after the database.
tar -C "$TREASURY_DATA_DIR" -czf /backup/treasury/data-$(date +%F).tar.gz ingest source-files
```

Database first, then the files: a statement imported in between leaves an extra Source file in the backup, which is
harmless, whereas the other order could miss a file that a Transaction in the dump links to.

A dump file starts with `PGDMP`; `pg_restore --list` on it shows its contents.

### Every night (cron)

`/usr/local/bin/treasury-backup` (`chmod 700`), with your project name and paths:

```sh
#!/bin/sh
set -eu
PROJECT=aspire-compose-c234f4ee
DEPLOY=/home/me/treasury/deploy
DATA=/srv/treasury/data
DEST=/backup/treasury
KEEP_DAYS=30

STAMP=$(date +%F)
mkdir -p "$DEST"
cd "$DEPLOY"
docker compose -p "$PROJECT" --env-file .env.Production exec -T postgres \
  sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" pg_dump -U postgres -Fc treasurydb' > "$DEST/treasury-$STAMP.dump.part"
mv "$DEST/treasury-$STAMP.dump.part" "$DEST/treasury-$STAMP.dump"
tar -C "$DATA" -czf "$DEST/data-$STAMP.tar.gz" ingest source-files
find "$DEST" -name 'treasury-*.dump' -mtime +"$KEEP_DAYS" -delete
find "$DEST" -name 'data-*.tar.gz' -mtime +"$KEEP_DAYS" -delete
```

```cron
# crontab -e (as a user in the docker group): every night at 02:30
30 2 * * * /usr/local/bin/treasury-backup >> /var/log/treasury-backup.log 2>&1
```

`/backup/treasury` should be on another disk, and copied off the machine (another NAS, a USB disk, or
restic/borg/rclone to cloud storage): a backup on the same disk doesn't survive a disk failure. If you already back
up the data folder with a NAS snapshot or restic, drop the `tar` line and back up the dump folder with it.

Try a restore now and then, e.g. on a laptop with Docker Desktop.

## Restoring

The same steps restore onto the existing installation (after a mistake) or onto a new machine.

1. **A running installation.** On a new machine follow [deployment.md](deployment.md) steps 1 to 4 first. The
   passwords you give there needn't match the old ones: the new installation creates a first Admin, but the
   restore replaces it with the old Members, who log in with their old passwords.

2. **Stop the API**, so nothing writes while you restore:

   ```bash
   dc stop api
   ```

3. **Replace the database** with the dump:

   ```bash
   dc exec -T postgres sh -c 'export PGPASSWORD="$POSTGRES_PASSWORD";
     dropdb -U postgres --if-exists --force treasurydb && createdb -U postgres treasurydb'
   dc exec -T postgres sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" pg_restore -U postgres -d treasurydb --no-owner' \
     < /backup/treasury/treasury-2026-11-20.dump
   ```

4. **Replace the data folder** with the matching backup, and give it back to uid 1654 (Linux):

   ```bash
   sudo rm -rf "$TREASURY_DATA_DIR/ingest" "$TREASURY_DATA_DIR/source-files"
   sudo tar -C "$TREASURY_DATA_DIR" -xzf /backup/treasury/data-2026-11-20.tar.gz
   sudo chown -R 1654:1654 "$TREASURY_DATA_DIR"
   ```

5. **Start the API** and check:

   ```bash
   dc start api
   curl http://localhost:8080/api/health
   ```

   Log in with an old Member's password; Accounts, balances, Transactions and the Source file downloads are back.
   A dump from an older version is fine: the API applies newer migrations at startup.
