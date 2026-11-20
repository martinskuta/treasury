# A Rule acts with the reach of the Member who last saved it, limited to the Accounts that Member can see

Rules are shared: every Member may create, edit, reorder and delete any Rule. Each Rule still records an author, and the author decides which Transactions the Rule may touch. The author is **the Member who last created or edited the Rule** (reordering doesn't count). If the author were fixed at creation instead, Bob could edit Anna's Rule — change its text and its actions — and it would then run with Anna's reach, over Anna's personal Accounts that Bob can't see. Making the editor the author means saving a Rule never gives anyone more reach than they already have.

A Rule's scope is **the Accounts its author can see: the Accounts the author owns, plus the Family's Accounts**. It is evaluated each time the Rule runs (on import and on "Apply Rules"), using the shared Owner rule `OwnedByViewerOrFamily` on Accounts. This is narrower than Transaction visibility (`VisibleTo`), which also shows a Member the Transactions they own inside another Member's personal Account. We keep Rule scope Account-based because the spec phrases it that way, and because a Rule is a standing, automatic instruction: it should only act inside Accounts whose full contents its author can see. A single reassigned Transaction in Anna's card is not an invitation for Bob's Rules to work through Anna's statements. So a Transaction Bob owns inside Anna's personal Account is not reached by Bob's Rules, even though Bob can see and classify it by hand.

When the author is **deactivated, the Rule stops running** (`active: false` in the API, shown on the Rules page) until an active Member saves it and so becomes its author. We considered handing the Rule to whoever took over the deactivated Member's Accounts, or turning it into a Family Rule. Both would quietly change whose Accounts the Rule reaches without anyone deciding to.

## Consequences

- Editing a Rule can change what it reaches: if Bob edits a Rule Anna wrote, it stops touching Anna's personal Accounts and starts touching Bob's. The Rules page shows each Rule's author so this is visible.
- A deactivated Member's Rules stay in the list, inactive, so nobody loses the conditions they encoded. Saving one makes it run again with the saver's reach.
- If Rule scope ever has to follow Transaction visibility instead, change `RuleEngine`'s reach lookup and this ADR together.
