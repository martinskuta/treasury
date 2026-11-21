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

The compose files are generated from the AppHost (ADR-0004), never edited by hand. To install Treasury at home,
follow [docs/deployment.md](docs/deployment.md) (parameters, data folder, first login, network share, updates) and set
up backups with [docs/backup.md](docs/backup.md) (pg_dump, data folder, restore).

```bash
aspire publish -o deploy    # writes deploy/docker-compose.yaml and deploy/.env
aspire deploy -o deploy     # also builds the image and runs docker compose up
```

The app is served on `http://localhost:${TREASURY_HTTP_PORT:-8080}` (API and React app in one container) and the
Aspire dashboard on port 18888. `deploy/` holds secrets (`.env.Production`) and is git-ignored.

## Data folder

Ingest folders and kept Source files live in the data folder, the API's `Storage:DataRoot`:

- `aspire run`: the repository's git-ignored `data/` folder.
- Docker Compose: the host folder `${TREASURY_DATA_DIR:-./data}` (relative to the compose file), bind-mounted at
  `/data` in the API container. On Linux it must be writable by the container user (uid 1654):
  `chown -R 1654:1654 data`.

Creating an Account creates `ingest/<account-slug>/` with `processed/` and `failed/` inside (ADR-0002). Back up
this folder together with the database ([docs/backup.md](docs/backup.md)).

- `ingest/<account-slug>/processed/`: files imported successfully (uploaded ones are placed here too).
- `ingest/<account-slug>/failed/`: files that couldn't be imported, each with a `<file>.error.txt` note saying why.
  Nothing from such a file was imported; fix it and import it again.
- `source-files/<account-slug>/<id>/<file>`: Treasury's own copy of every imported Source file. Transactions link
  to it and the app downloads it from here, so don't edit or delete these by hand.

## Importing statements

On the Accounts screen, choose **Import statement** on an Account and upload the bank's export. The Account's
Integration reads it. Or drop the file into the Account's Ingest folder, `ingest/<account-slug>/`: Treasury checks
every Ingest folder at startup and then every minute, imports each file it finds there through the same pipeline,
and moves it to `processed/` or `failed/`. A file must stay unchanged for 2 seconds during a check before it is
imported, so a file that is still being copied in is left for the next check; temporary download names (`.part`,
`.crdownload`, `.tmp`, ...) and hidden files are ignored until renamed. Settings: `Ingest__ScanInterval` (default
`00:01:00`) and `Ingest__SettleTime` (default `00:00:02`). `POST /api/ingest/scan` checks the folders right away.
Imported date, amount, currency, description and original foreign amount are read-only;
Display name, note and Owner can be edited.

Two safety checks run on every import:

- **Identifier**: when the Account has an identifier (IBAN or card number) and the statement carries one, they must
  match, otherwise the file goes to `failed/` and nothing is imported. Spaces, `-` and letter case are ignored
  (`CH93 0076 2011 6238 5295 7` = `ch9300762011623852957`). Card numbers (digits, optionally masked with `*`, `X`,
  `•` or `.`) match on their last 4 digits, so an Account identifier `1234` matches `5413 **** **** 1234`.
  The check is skipped when either side has no identifier.
- **Closing balance**: when a statement includes a closing balance, it is compared with the Account's calculated
  balance on that date. The Accounts screen shows a warning while the most recent statement's closing balance
  doesn't match (a missing or doubled import); it disappears once the balances match again.

### Generic CSV (`generic-csv`)

Until there is an Integration for your bank, convert its export (e.g. in a spreadsheet) to this CSV and create the
Account with the **Generic CSV** Integration:

```csv
date,amount,description,counterparty,reference,original_amount,original_currency
2026-11-02,-12.50,COOP-1234 ZURICH HB,Coop,TX-0001,,
2026-11-03,5000.00,"SALARY NOVEMBER, ACME AG",ACME AG,TX-0002,,
2026-11-05,-46.20,TRATTORIA ROMA,Trattoria Roma,TX-0003,-48.00,EUR
```

- UTF-8 (a byte order mark is fine). The first line is the header; column names are case-insensitive and may come
  in any order. Blank lines are ignored.
- Separator `,` or `;` (whichever the header uses). Quote values that contain the separator: `"Coop, Zurich"`;
  a quote inside a quoted value is doubled: `"Bar ""Da Mario"""`. A quoted value can't span lines.
- Required columns:
  - `date`: `2026-11-02` or `02.11.2026`
  - `amount`: in the Account's currency, `.` as the decimal point, `'` thousands separators allowed;
    negative = money out (Expense), positive = money in (Income)
  - `description`: the bank's text
- Optional columns:
  - `counterparty`: the other party
  - `reference`: the bank's unique id for the line
  - `currency`: if present it must be the Account's currency (otherwise the file is rejected)
  - `original_amount` and `original_currency` (both or neither): for a purchase in a foreign currency, what was
    paid, e.g. `-48.00` and `EUR`, next to the `amount` the Account was charged
- No Account identifier and no closing balance, so Accounts using it can't have an identifier.

### TKB (`tkb`)

For Thurgauer Kantonalbank, download the account statement from TKB e-banking as **camt.053** (ISO 20022) and
import the file as it is: a single XML file or the zip (one or more XML statements; other files in the zip, such as
a PDF copy, are ignored). A zip is one Source file, so undoing its import removes all of it. TKB's CSV export is not
supported.

- Enter the IBAN as the Account's identifier to have statements for another account rejected.
- Each camt.053 `Ntry` with status `BOOK` becomes a Transaction; other statuses (such as `PDNG`) are skipped. A
  Transaction gets the bank's booking date (`BookgDt`), the amount (money in positive, money out negative), the
  bank's reference (`AcctSvcrRef`), the other party as counterparty (the recipient for money out, the sender for
  money in, preferring the ultimate party) and a description made of the bank's text (`AddtlNtryInf`), the
  counterparty and the remittance message or reference, one per line. A foreign-currency purchase keeps its
  original amount and currency (`InstdAmt`).
- A `Ntry` that bundles several transfers (several `TxDtls`) becomes one Transaction per `TxDtls`, each dated with
  the bank's booking date and keeping the `TxDtls`' own reference, if any. If a `TxDtls` has no amount, or the
  amounts don't add up to the `Ntry`'s amount, the `Ntry` is imported as one Transaction instead.
- The closing balance of the statement that ends last is used for the balance check.
- Rejected (nothing imported): a statement whose opening balance (`OPBD` or `PRCD`) plus its money in and out with
  status `BOOK` doesn't equal its closing balance (`CLBD`), a page of a multi-page statement that isn't the last,
  statements for different accounts or currencies or with overlapping periods in one zip (statements that only
  touch, one ending when the next starts, are fine; without `FrToDt` a statement runs from its first booking date to
  its closing balance date), a zip without XML or larger than 50 MB unpacked, and anything that isn't camt.053 (CSV,
  camt.052, camt.054).

### Revolut (`revolut`)

For Revolut, export the account statement from the Revolut app as **CSV (Excel)** in **English** and import the
file as it is. Its header is
`Type,Product,Started Date,Completed Date,Description,Amount,Fee,Currency,State,Balance` (a UTF-8 byte order mark
is fine). One Revolut account holds several currencies, and a file can contain all of them.

- Revolut statements have no account identifier, so Accounts using it can't have an identifier.
- Only rows with Product `Current` and State `COMPLETED` are imported; everything else (savings and other
  `Deposit` rows, `PENDING`, `REVERTED`, `DECLINED`, ...) is skipped. Type and State may be written in title case
  (`Card Payment`) or, in older exports, in upper case with underscores (`CARD_PAYMENT`, `TOPUP`).
- A Transaction gets the Completed Date, the `Amount` (money in positive, money out negative) in its `Currency`,
  and Revolut's `Description` as it is. Revolut has no reference, so the description is part of what makes a
  Transaction a duplicate. The counterparty is the description for `Card Payment` and `Card Refund`, the
  description without a leading `To ` / `From ` for `Transfer`, and empty for anything else (top-ups, exchanges,
  ...).
- A non-zero `Fee` becomes a second Transaction on the same date and in the same currency: minus the fee, described
  as `Fee: <description>`, with counterparty `Revolut` (Revolut's balance is the previous balance + `Amount` −
  `Fee`).
- For each currency, the `Balance` of its last imported row (in Completed Date order) is that currency's closing
  balance on that row's Completed Date, and the balance before its first row (first `Balance` − `Amount` + `Fee`)
  is where that currency stood before the file.
- Rejected (nothing imported): a header in another language (export the statement in English), a line with more
  or fewer fields than the header, and an imported row whose Completed Date, Amount, Fee or Balance can't be read
  or whose Currency is empty. The error says which line.
