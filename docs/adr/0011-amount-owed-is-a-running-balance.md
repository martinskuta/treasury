# What Owners owe each other is a running balance paid down oldest first, not links between a Reimbursement and the Expenses it covers

A Member often pays a Family Expense from a personal Account and the Family pays them back, either at once or later with one payment covering several purchases. Treasury tracks this as an **Amount owed** per pair of Owners, a single running figure in CHF. Every Transaction on an Account of one Owner that belongs to the other moves it by the Transaction's amount (an Expense raises what is owed, an Income lowers it), and every **Reimbursement** between the two pays it down. Which Transactions count as paid back is worked out, never stored: Reimbursements are applied to the oldest Transactions first.

The obvious alternative was to link each Reimbursement to the Expenses it covers, one-to-one or one-to-many, extending the pair link Transfers and Contributions already use. We decided against it because the Family's paybacks are close to the sum of the purchases but not always exact. Strict links would either refuse those paybacks or need a tolerance plus somewhere to put the difference, and every payback would need a Member to pick its purchases by hand. A running balance takes any amount: whatever a Reimbursement leaves over, in either direction, stays owed and is absorbed by the next one.

A Reimbursement counts as neither Income nor Expense for either Owner. The Expense it pays back is already counted for the Owner it was made for, on the day it was made, in its real Category. Before this decision the payback was a Contribution, so a Family Expense paid from a personal Account was counted twice in the Family's figures, and the Member got an Income they never had.

In bank data a Reimbursement and a Contribution look the same, so the direction sets the default: money from the Family to a Member is a Reimbursement, money from a Member to the Family is a Contribution (such as a monthly share), and money between two Members is a Contribution. A Member can switch any pair by hand. We considered deciding by whether anything is owed at the time, but a payback is often imported before the purchases behind it are marked as the Family's, and the pair would then be classified wrongly.

The balance is kept in CHF only, each Transaction converted at the rate of its own date, so a CHF Reimbursement can pay down an Expense made in EUR. Exchange-rate differences end up as small leftovers.

## Consequences

- Treasury cannot say which purchases a given Reimbursement was meant for, only the order in which Transactions are paid back. If the Family ever pays back out of order on purpose, the "paid back" markers will not reflect that intent, though the Amount owed stays right.
- Changing a Transaction's Owner, or adding an older Transaction, shifts which later Transactions count as paid back.
- Only Transactions from a start date the Family sets count towards the Amount owed. Without it, imported history would show old paybacks as Reimbursements against purchases nobody marked as the Family's.
- A Reimbursement needs both sides in Treasury as a matched pair. Paying back in cash, or to an Account Treasury does not track, cannot be recorded as one.
- A Transaction belongs to one Owner as a whole. A purchase that is partly for the Family cannot be shared until Transactions can be split.
- Small leftovers can stay owed indefinitely. If that becomes a nuisance, add a way to write a leftover off as a Contribution.
