# API tests run the API in-process with WebApplicationFactory against Testcontainers PostgreSQL

The HTTP API tests (seam 1) run the real API in-process with `WebApplicationFactory` against a real PostgreSQL container started by Testcontainers, and use TUnit. Aspire's testing host would start the whole AppHost (API as a separate process, the dashboard and the Vite app), but then tests can't reach the API's dependency injection. The spec requires fakes for the clock, the exchange-rate provider and a test Integration behind the API, and a fake clock that tests can move forward during a test. With `WebApplicationFactory` these are swapped in `ConfigureTestServices`.

One PostgreSQL container is shared by the whole test session. Migrations run once into a template database, and each test gets its own database cloned from it (`CREATE DATABASE … TEMPLATE …`) and its own API instance, so tests stay isolated and can run in parallel.

## Consequences

- Docker must be running to run `dotnet test`.
- The tests don't cover the AppHost wiring itself (environment variables, the Vite proxy). A single Aspire-hosted smoke test could be added later if needed.
