# PostgreSQL as the database

Treasury stores its data in PostgreSQL, run as its own container next to the app in docker-compose. SQLite (one file in a volume) would be enough for one family's data and simpler to back up, but PostgreSQL is the most mature option with EF Core and leaves room for the planned investment tracking and heavier reporting without a migration.
