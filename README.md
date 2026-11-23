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

## Currencies

An Account (and a Multi-currency account's main currency) can be in any currency Treasury can get an exchange rate
for: EUR and the ECB reference rates served by [Frankfurter](https://frankfurter.dev) — AUD, BRL, CAD, CHF, CNY,
CZK, DKK, GBP, HKD, HUF, IDR, ILS, INR, ISK, JPY, KRW, MXN, MYR, NOK, NZD, PHP, PLN, RON, SEK, SGD, THB, TRY, USD
and ZAR. The list lives in `src/Treasury.Api/Accounts/Currencies.cs` and is served to the app at `GET
/api/currencies`. Amounts are shown in CHF using the most recent rate on or before their date; rates are fetched in
the background for every supported currency, back to the earliest Transaction or opening balance (a currency added
to the list is backfilled over that whole range).

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

- **Identifier**: when the Account (or the Multi-currency account) has an identifier (IBAN or card number) and the
  statement carries one, they must match, otherwise the file goes to `failed/` and nothing is imported. Spaces, `-`
  and letter case are ignored (`CH93 0076 2011 6238 5295 7` = `ch9300762011623852957`). Card numbers (digits,
  optionally masked with `*`, `X`, `•` or `.`) match on their last 4 digits, so an Account identifier `1234`
  matches `5413 **** **** 1234`. The check is skipped when either side has no identifier.
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

### camt.053 banks: TKB (`tkb`)

TKB (Thurgauer Kantonalbank) is read from its ISO 20022 **camt.053** statement: download the account statement from
TKB e-banking as camt.053.

Import the file as it is: a single XML file or the zip (one or more XML statements; other files in the zip, such as
a PDF copy, are ignored). A zip is one Source file, so undoing its import removes all of it. The bank's CSV export
is not supported.

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

### PostFinance (`postfinance`)

PostFinance offers no camt.053 for a private account, so it is read from the **Kontoauszug** PDF it delivers
(ADR-0012): download the account statement as **PDF** from PostFinance e-finance, in **German**, and import the file
as it is. A statement of any period works (monthly, quarterly, yearly), also one without movements.

- Enter the IBAN as the Account's identifier to have statements for another account rejected. The statement's
  currency (printed after the IBAN) must be the Account's.
- Each row of the table becomes a Transaction, dated with its booking date (`Datum`, which the statement prints on a
  day's first row only; `Valuta` is ignored), with `Gutschrift` as money in (positive) and `Lastschrift` as money
  out (negative). The description is the row's text block as printed, one line per printed line; words the
  statement wraps (`AUFTRAG CH-DD-` / `BASISLASTSCHRIFT`) are not rejoined.
- The counterparty is the line after `ZAHLUNGSEMPFÄNGER:`, `ABSENDER:` or `AUFTRAGGEBER:`, for a TWINT purchase the
  merchant line (after `VOM <date>`), and `PostFinance` for a price it charges. It is empty otherwise, for example
  for a plain `LASTSCHRIFT`, where the statement doesn't say which line is the payee.
- A price PostFinance charges (a row starting with `PREIS FÜR`) gets the Tag `fee`, put there by the import as for
  Revolut's fees.
- PostFinance Transactions have no bank reference (the ids the statement prints exist on some kinds of row only and
  stay in the description), so the description is part of what makes a Transaction a duplicate. Importing the same
  or an overlapping statement again is safe.
- The closing `Kontostand` is used for the balance check.
- The original amount and currency of a foreign-currency purchase are not read yet; what the statement prints
  about them stays in the description.
- Rejected (nothing imported): a statement that doesn't agree with itself (the opening `Kontostand` plus the rows
  must equal the closing `Kontostand`, the `Gutschrift` and `Lastschrift` amounts must add up to the `Total` line,
  and every printed `Saldo` must equal the balance after its row), a table line that is neither a row nor part of a
  row's text (the message names the page and the line), and anything that isn't a Kontoauszug PDF in German
  (camt.053, CSV, the credit-card invoice, another PDF, a statement in French, Italian or English).

### Revolut (`revolut`)

For Revolut, export the **Custom statement** from the Revolut app (Profile → Documents & statements → Custom
statement) as **PDF** in **English**, for full past months, and import the file as it is. One PDF covers every
currency of a Revolut login for the chosen period, so Revolut is used through a **Multi-currency account**: create
one with the Revolut Integration and a main currency, then drop the PDF into its Ingest folder (`ingest/<slug>/`)
or upload it from its **Imports** on the Accounts screen. A Member with their own Revolut login creates their own
Multi-currency account. Revolut's CSV account statement is not supported.

- Enter your Revolut IBAN as the Multi-currency account's identifier to have another login's statement rejected.
  The Accounts in it have no identifier of their own.
- Read from the PDF: the period, and for each "Personal Account (CUR)" its IBAN, opening balance, closing balance
  and transaction table. Everything else (investments, savings, crypto, the "Information about ..." pages) is not
  read; when such a section has entries, the import result says so in a warning. A current account that isn't a
  Personal Account, e.g. a "Joint Account (EUR)", is not imported either (neither its summary nor its transactions,
  and its IBAN is ignored); a warning names it.
- Each row goes to the Multi-currency account's Account in the currency of the row's "Personal Account (CUR)". A
  currency without an Account yet gets one during the import when the statement shows money in it (a row, or an
  opening or closing balance other than zero), named `<Multi-currency account name> <CUR>` (renamable), with the
  Multi-currency account's Owner, opened on the first day of the statement's period with the statement's
  opening balance. A currency with no rows and a zero balance gets no Account. The import result says "Created
  Account ...". Only currencies Treasury has exchange rates for can have Accounts (see [Currencies](#currencies)):
  a statement with rows in another currency is rejected; a balance without rows in another currency is skipped,
  with a warning that names the currency and says its balance isn't tracked.
- A Transaction gets the row's Date, its Money in/out (money in positive, money out negative) in the account's
  currency (the converted amount printed beneath it is ignored) and its Description as it is; a description that
  wraps over several lines is joined with spaces. The counterparty is the description when Revolut's Category is
  `Merchant`, `Revolut` when it is `Fees` (a charge by Revolut itself, such as a card delivery fee), the
  description without a leading `To ` / `From ` when it has one, and empty otherwise. Revolut's Category is
  otherwise ignored. Every row in the table is imported.
- Revolut's Money in/out includes the row's fee; the Fees column says how much of it is the fee. A non-zero Fees
  value is taken out into a second Transaction on the same date and in the same currency: minus the fee, described
  as `Fee: <description>`, with counterparty `Revolut`. The first Transaction is Money in/out without the fee, so
  the two add up to Money in/out (a withdrawal of -107.14 with a fee of 1.41 becomes -105.73 and -1.41).
- A fee gets the Tag `fee`, put there by the import: the `Fee: <description>` Transaction taken out of a row, and a
  row whose Revolut Category is `Fees` (such as a card delivery fee). Nothing else is tagged. The Tag is found by
  name, ignoring case, and created when it doesn't exist yet (a plain Tag: no date range, no Owner limit). Removing
  it from a Transaction by hand sticks; undoing the import keeps the Tag itself.
- The file is one Source file. Rules, date-range Tags and the balance check work per Account as usual: each
  currency's closing balance, on the last day of the period, is compared with its Account's calculated balance,
  also for a currency with no rows in the period, and also when that balance is zero. An exchange between two of
  its currencies is paired as a Transfer when both legs' CHF values are within 2% and at most a day apart (exchange
  rates for that day must be known). Undoing the import removes its Transactions in every currency; Accounts it
  created stay.
- Revolut has no reference, so the description is part of what makes a Transaction a duplicate. Importing the same
  PDF again, or one whose period overlaps an earlier one, skips the rows already imported.
- Files go to the Multi-currency account's `processed/` or `failed/`. Its Accounts have no Ingest folder of their
  own, and uploads straight into one of them are rejected.
- Rejected (nothing imported), with an error that names the currency and the figures that disagree: a row whose
  Balance isn't the previous balance (the opening balance for the first row) + Money in/out, a currency
  whose last Balance isn't its closing balance (or whose opening and closing balance differ when it has no rows),
  a currency whose rows' Money in/out or Fees don't add up to its Total row, a non-zero "Tax withheld" or "Other
  taxes" (not supported yet; the error names the amount and its row's date, or the Total row), an account that is
  in the summaries but not in the transaction statements or the other way round, the same currency twice (a table
  that continues on the next page under its account's heading printed again is one table), more than one IBAN in
  one statement (accounts without one are fine), an IBAN other than the Multi-currency account's identifier, and
  anything that isn't a Revolut Custom statement PDF in English (a CSV, another PDF, a statement in another
  language).
