# PostFinance is read from the Kontoauszug PDF, not from camt.053

The `postfinance` Integration was first built on the camt.053 reader shared with TKB, before anyone had a real PostFinance export. It turned out that PostFinance does not offer camt.053 for the Family's private account, so that path could never be used. The document PostFinance does deliver every month is the **Kontoauszug** PDF, so the `postfinance` Integration reads that PDF and nothing else; its camt.053 reading is removed. TKB keeps camt.053.

Reading a PDF is fragile, so the reader follows ADR-0010 and trusts nothing it can't verify. A file is rejected unless the opening Kontostand plus the rows equals the closing Kontostand, the Gutschrift and Lastschrift sums equal the printed Total, and every printed Saldo equals the running balance at its row. The Saldo is printed once per booking date, not per row, so it is the only figure that catches an amount read into the wrong row.

## Considered options

- **Keeping camt.053 alongside the PDF** under the same key, or the PDF under a second key: rejected, for the reason ADR-0010 gives for Revolut's CSV. The PDF has no reference, so the same payment read from two formats would have to be proven identical row by row, and a reader nobody can use would be maintained.
- **Using the references the PDF prints** (`TRANSAKTIONS-ID`, `SENDER REFERENZ`, `REFERENZEN`) as the bank reference: rejected. Only some kinds of row have one (TWINT purchases and fees have none), so one Account would find duplicates in two different ways. They stay in the description, where they make those rows unique anyway.

## Consequences

- PostFinance Transactions have no bank reference; duplicates are found by date, amount and description.
- The description is the statement's text block as printed, line by line; wrapped words are not rejoined. The counterparty is filled only where the statement labels it (`ZAHLUNGSEMPFÄNGER:`, `ABSENDER:`, `AUFTRAGGEBER:`) or its position is fixed (TWINT), and is empty otherwise.
- The reader depends on PostFinance's PDF layout and on German. When the layout changes, imports fail loudly with a reconciliation error; they do not import wrong numbers.
- Only the Kontoauszug is read. The credit-card invoice and other PostFinance documents are rejected.
- The real statements hold personal data and are never committed; tests build synthetic PDFs.
