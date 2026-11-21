# Treasury

A family finance tracker: records the income and spending (and, later, the investments) of a family as a whole and of each of its members.

## Language

### People

**Family**:
The group of people whose finances Treasury tracks together. An installation of Treasury holds exactly one Family.
_Avoid_: Household, group, tenant, organization

**Member**:
An adult who belongs to the Family and whose finances can be tracked individually. A Member logs in with a **username** and has a required **first name** and an optional **last name**, which they or an Admin can change; wherever Members are listed, Treasury identifies one as first name, last-name initial and username (for example "Martin Š. (martin)"), so two Members with the same first name stay distinguishable. Members are never deleted; an Admin **deactivates** a Member, which stops their login but keeps their data, and can hand their Accounts to the Family or another Member.
_Avoid_: User, person, family member

**Admin**:
A role a Member can hold that lets them manage the other Members' logins (add, deactivate, reset passwords, give or remove the Admin role). The first Member created in an installation is an Admin; there is always at least one active Admin. The role gives no view into other Members' personal finances.
_Avoid_: Superuser, root, administrator account

### Money

**Account**:
A place where the Family holds money in a single currency, such as a bank account, card or cash wallet. Every Account has exactly one Owner.
_Avoid_: Wallet, pocket, bank account (when meaning any Account)

**Multi-currency account**:
A group of Accounts of one Owner that one provider holds together, one Account per currency (for example a Revolut login with CHF, EUR and USD). It has a main currency, in which its total balance is shown, and gets an Account for a new currency automatically when a statement first contains that currency. It is not itself an Account, and the Accounts in it are ordinary Accounts.
_Avoid_: Account group, parent account, wallet, sub-account (for the Accounts in it)

**Owner**:
The Member, or the Family as a whole, that an Account or Transaction belongs to. A Transaction's Owner defaults to its Account's Owner but can be changed to the Family or any Member.
_Avoid_: Payer, holder, assignee

**Transaction**:
A single movement of money recorded in Treasury: an Income, an Expense or a Transfer.
_Avoid_: Entry, record, movement, booking

**Income**:
A Transaction that brings money to an Owner from outside that Owner, including money another Member or the Family sends them.
_Avoid_: Revenue, earning, credit

**Expense**:
A Transaction that sends money away from an Owner, including money they send to another Member or to the Family.
_Avoid_: Spending, cost, payment, debit

**Transfer**:
A Transaction that moves money between two Accounts of the same Owner; it is neither Income nor Expense.
_Avoid_: Internal payment, move

**Contribution**:
A matched pair of Transactions in which one Owner sends money to another (for example a Member paying into a Family Account): an Expense for the sender and an Income for the receiver.
_Avoid_: Transfer (for money between different Owners), allowance, deposit

**Display name**:
The friendly, editable name shown for a Transaction in place of the bank's original description, which is kept unchanged.
_Avoid_: Title, label, alias, payee name

### Classification

**Category**:
A node in the Family's tree of spending and income kinds (for example Food › Eating out). Every Income and Expense has exactly one Category; Transfers have none.
_Avoid_: Type, group, bucket, label

**Tag**:
A free-form marker that cuts across Categories (for example `holiday-italy-2026`); a Transaction can carry any number of Tags. A Tag may have a date range so that it is applied automatically.
_Avoid_: Label, keyword, event

**Rule**:
A condition on incoming Transactions (such as "description contains MIGROS") together with the Category, Tags or Owner it assigns. Rules never override a manual edit.
_Avoid_: Filter, automation, mapping

**Uncategorised**:
The state of an Income or Expense that has no Category yet because no Rule matched; such Transactions wait for review.
_Avoid_: Unclassified, inbox, pending

### Ingestion

**Integration**:
A supported source of statements (such as a particular bank) whose exports Treasury knows how to read.
_Avoid_: Importer, connector, provider, plugin

**Ingest folder**:
The folder belonging to one Account, or to one Multi-currency account, where statement files are dropped to be imported into it. For a Multi-currency account, each row goes to its Account in the row's currency.
_Avoid_: Inbox, drop folder, import folder, consume folder

**Source file**:
An imported statement file, kept after import so every Transaction can be traced back to the file it came from.
_Avoid_: Statement, upload, document
