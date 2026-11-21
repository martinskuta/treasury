# A Multi-currency account shares one Ingest folder and one Source file across its per-currency Accounts

Revolut holds one balance per currency under a single login, and exporting and dropping a statement per currency into a folder per Account (ADR-0002) is too much hassle. So an Integration that declares it holds several currencies (only `revolut` for now) is used through a **Multi-currency account**: a group of ordinary single-currency Accounts of one Owner, with a main currency in which the group's total balance is shown. The Multi-currency account, not each Account, has the Ingest folder and is the upload target. Each statement row goes to the Account in the row's currency, and an Account for a currency the group doesn't have yet is created during import, with its opening balance taken from the balance the statement reports before that currency's first imported row. This amends ADR-0002: the folder still pins down the Owner and the Integration, and within the group the row's currency pins down the Account.

A dropped file stays **one Source file**, even when its rows land in several Accounts. Undoing it removes every Transaction it created in every currency. Accounts it created are kept. The Source file keeps one closing balance per currency, each checked against its own Account.

## Considered options

- **Making Account itself multi-currency**: rejected. Balances, the closing balance check, duplicate detection and Transfer matching all assume one currency per Account, and "Account = one currency" is part of the glossary and the spec.
- **One Ingest folder per Account, as for single-currency banks**: rejected because every currency would need its own export and drop.
- **Splitting a multi-currency file into one Source file per Account**: rejected to keep undo simple. One dropped file is one thing to undo.
- **Failing a file that contains a currency with no Account**: rejected because Revolut's running balance makes creating the Account safe and automatic.

## Consequences

- The import pipeline's "one file into one Account" assumption no longer holds for Multi-currency accounts: rows are routed by currency, and a Source file can own Transactions in several Accounts.
- Owner changes, including handing over a deactivated Member's Accounts, apply to the Multi-currency account as a whole. Its Accounts can be renamed but not detached, moved or added by hand.
- A currency exchange inside a Multi-currency account is two rows in two of its Accounts and is paired as a Transfer by the normal cross-currency matching.
