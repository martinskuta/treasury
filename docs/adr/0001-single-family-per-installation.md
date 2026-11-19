# One Family per installation

Each Treasury installation (one Docker deployment) holds exactly one Family. Treasury is self-hosted by the family that uses it, so supporting several Families would add data isolation, sign-up and per-Family administration everywhere for no real benefit. Another family that wants Treasury runs its own installation.

## Consequences

- There is no tenant or Family identifier to scope queries by: all data in the database belongs to the one Family.
- Going multi-Family later would mean adding that scoping to every table and query.
