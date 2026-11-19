# Aspire for local orchestration and Docker deployment

The solution is composed with Aspire. An AppHost project describes the API, the React frontend, PostgreSQL and the volumes for Ingest folders and Source files. It runs everything locally with one command and the Aspire dashboard. Its Docker integration publishes the same model as the docker-compose deployment, so there is one description of the system rather than a hand-maintained compose file that can drift from the local setup.

## Consequences

- The docker-compose files are generated from the AppHost, not edited by hand. Deployment changes (volumes, environment variables, ports) are made in the AppHost.
- Service defaults (health checks, OpenTelemetry, resilience) come from Aspire's ServiceDefaults project.
