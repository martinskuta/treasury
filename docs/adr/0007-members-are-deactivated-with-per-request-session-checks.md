# Members are deactivated, not deleted, and sessions are re-checked on every request

An Admin deactivates a Member instead of deleting them, so their Accounts, Transactions and history stay intact (Owner foreign keys use `Restrict`). Deactivation sets `Member.DeactivatedAt`; our `TreasurySignInManager` refuses password login for a deactivated Member and fails their security-stamp check, so their open sessions end too. We chose an explicit column over Identity's lockout (`LockoutEnd = MaxValue`) because lockout is also used for failed-login throttling and would make "deactivated" ambiguous.

Identity normally re-validates the cookie's security stamp only every 30 minutes. We set the interval to zero: each authenticated request does one small user lookup, and in exchange a deactivation, password reset or Admin-role change takes effect on open sessions at once (the cookie principal, including role claims, is rebuilt on every check). For a single-family installation the extra query is negligible.

When deactivating, the Admin may hand each of the Member's Accounts to the Family or another active Member. To choose, the Admin sees only the names and currencies of that Member's Accounts (`GET /api/admin/members/{id}/accounts`), never balances, identifiers or Transactions; the Admin role otherwise gives no view into other Members' personal finances. Accounts not handed over stay with the deactivated Member.

## Consequences

- An Admin can't deactivate themselves, and the last active Admin can't lose the role, so there is always an Admin who can log in.
- Code that offers "any Member" as an Owner should offer only active Members.
