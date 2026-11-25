# Statements are ingested from a per-Account Ingest folder

Transactions mostly get into Treasury when someone drops exported statement files (CSV, Excel, PDF and so on) into an Ingest folder, not through live bank APIs. Each Account is created in the app first, and creating it also creates its Ingest folder. A file dropped there is imported into that Account, and its Transactions take the Account's Owner as their default Owner. The web app's upload screen feeds the same pipeline.

We chose this so that adding a bank only means writing a reader for its export format (an Integration). There's no third-party aggregator, no bank credentials and no API to depend on. Because the folder already says which Account (and so which Owner) a file belongs to, the file itself doesn't have to identify them. Live bank integrations can still be added later as another way to feed the same import pipeline.

When a statement does name its account (an IBAN or card digits), that Identifier only guards against a file dropped into the wrong folder: it never chooses the Account. An Account or Multi-currency account can have several Identifiers, because banks change them (Revolut moved its customers from GB to LT IBANs, and a reissued card has new digits), and a statement is accepted when it shows at least one of them. Identifiers are learned instead of typed: the first import into an account with none takes every Identifier its statement shows, and every successful import adds those the account doesn't have yet, since a statement covering a bank's change shows the old and the new IBAN together. An Identifier belongs to one account only, so a wrong file dropped first into a new account is rejected rather than learned. A statement showing only Identifiers the account doesn't have is rejected and teaches nothing; the Member adds the new Identifier by hand.

## Considered options

- **Detecting Accounts automatically from statement content (IBAN or card number)**: rejected, because not every format includes an identifier, and choosing the Account explicitly makes ownership unambiguous.
- **One folder per Owner and Integration**: rejected in favour of one folder per Account, which pins down both the Account and the Integration.
- **A single Identifier per account, edited when the bank changes it**: rejected, because statements for periods before the change still show the old one and would then be rejected.
- **Identifiers with a validity period, checked against the statement's period**: rejected. It is stricter, but the Member would have to find out when the bank switched, and an undated set already catches a file dropped into the wrong folder.
