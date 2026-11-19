# Statements are ingested from a per-Account Ingest folder

Transactions mostly get into Treasury when someone drops exported statement files (CSV, Excel, PDF and so on) into an Ingest folder, not through live bank APIs. Each Account is created in the app first, and creating it also creates its Ingest folder. A file dropped there is imported into that Account, and its Transactions take the Account's Owner as their default Owner. The web app's upload screen feeds the same pipeline.

We chose this so that adding a bank only means writing a reader for its export format (an Integration). There's no third-party aggregator, no bank credentials and no API to depend on. Because the folder already says which Account (and so which Owner) a file belongs to, the file itself doesn't have to identify them. Live bank integrations can still be added later as another way to feed the same import pipeline.

## Considered options

- **Detecting Accounts automatically from statement content (IBAN or card number)**: rejected, because not every format includes an identifier, and choosing the Account explicitly makes ownership unambiguous.
- **One folder per Owner and Integration**: rejected in favour of one folder per Account, which pins down both the Account and the Integration.
