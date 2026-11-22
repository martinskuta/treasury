# Revolut is read from the Custom statement PDF, not from CSV

Revolut Personal has no API, and its CSV account statement has to be exported one currency at a time, which made a monthly import of a Multi-currency account a chore. Revolut's **Custom statement** covers every currency account in one document, so the `revolut` Integration reads that PDF and nothing else; the CSV reader is removed. One export and one drop per month now covers all currencies.

Reading a PDF is fragile, so the reader trusts nothing it can't verify: the statement gives an opening balance, a closing balance and a total per currency and a running balance per row, and a file is rejected unless all of them agree with the rows that were read. The PDF also carries the IBAN, which the CSV never did, so a Multi-currency account can have an identifier that is checked on import like an Account's.

This amends ADR-0009. An Account for a new currency is opened from the statement's opening balance for the period (dated the first day of the period; an opening balance is the balance at the start of its date), not from a running balance before the first row, and a currency that has a balance but no rows in the period gets an Account too. A currency with no rows and a zero balance gets none.

## Considered options

- **Keeping the CSV reader alongside the PDF**: rejected. Neither format has a transaction reference, so duplicates are found by date, amount and description; two formats describing the same payment would have to be proven identical row by row, and a second reader would be maintained for a path nobody uses.
- **A browser extension driving app.revolut.com**, exporting each currency's CSV in the Member's own session: rejected once the Custom statement turned out to cover all currencies officially. It would break whenever Revolut changed its web app.
- **A PSD2 account-information aggregator** (Enable Banking's free personal tier is what Firefly III and Actual Budget use): rejected. It puts a third party between the Family and its bank, needs the consent renewed every 90 to 180 days, and its coverage of Swiss-resident Revolut customers was never confirmed.
- **Revolut's private web API** (as RevolVer does): rejected. It is outside Revolut's terms, its sessions expire within minutes, and it would put a live banking session in reach of Treasury. It is the only route with a stable transaction id, which is what was given up.

## Consequences

- Statements stay files dropped into an Ingest folder (ADR-0002); Treasury still holds no bank credentials.
- Revolut Transactions still have no bank reference, so the description remains part of what makes one a duplicate.
- The reader depends on Revolut's PDF layout and on English. When Revolut changes the layout, imports fail loudly with a reconciliation error; they do not import wrong numbers.
- Savings, investment and crypto sections of the Custom statement are not read.
