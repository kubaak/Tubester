You are helping me refactor my Tubester solution (.NET, API + Worker + tests) from **SQLite** to **PostgreSQL**.

## Context

- The solution currently uses **SQLite** as the primary database for both:
    - The **API** (Tubester.Api)
    - The **Worker** (Tubester.Worker)
- There is an extension like `services.AddDatabase(contentRootPath)` that configures EF Core with SQLite (probably
  `UseSqlite`), and there may be SQLite-specific connection strings or file paths (e.g., `.db` files in `App_Data` or
  under `contentRootPath`).
- EF Core migrations were generated for SQLite.
- For **local development**, I now run a **PostgreSQL** instance in Docker on `localhost:5432`.

  Typical docker example (for your context, don’t rewrite unless needed):

  ```bash
  docker run --name Tubester-postgres \
    -e POSTGRES_USER=Tubester \
    -e POSTGRES_PASSWORD=Tubester \
    -e POSTGRES_DB=Tubester \
    -p 5432:5432 \
    -d postgres:16

I want to completely remove all SQLite-specific code from the solution and use PostgreSQL everywhere (API + Worker +
Migrations). Tests can still use in-memory providers if needed, but no SQLite.

Goal
Replace EF Core SQLite provider with Npgsql PostgreSQL provider across the solution.

Remove all SQLite-specific configuration, packages, conditional branches, and file-based connection strings.

Introduce a clean, environment-based PostgreSQL configuration:

Local dev: PostgreSQL in Docker (localhost:5432).

Production: PostgreSQL via environment variables / connection strings.

Ensure migrations and runtime code all expect Postgres schema/behavior, not SQLite.

Constraints / Preferences
.NET + EF Core.

Prefer Npgsql.EntityFrameworkCore.PostgreSQL as the provider.

No dual-provider setup; I want a single DB provider (Postgres) for everything except tests.

Tests:

Integration tests should run against Postgres (can be Docker-based).

Unit tests can keep using in-memory providers.

Avoid partial half-migration; by the end, no UseSqlite, no SQLite connection strings, no references to SQLite.

Tasks
Identify all SQLite usage

Search for:

UseSqlite

Microsoft.EntityFrameworkCore.Sqlite

Data Source= style file paths.

Any .db file paths related to SQLite.

List all places where SQLite-specific code exists:

Startup / DI extensions (e.g. AddDatabase).

Connection string configuration in appsettings.*.json.

Migrations (if they contain SQLite-specific annotations).

Tests that rely on SQLite provider.

Update project references to Postgres

In all relevant .csproj files:

Remove Microsoft.EntityFrameworkCore.Sqlite (and tools if present).

Add Npgsql.EntityFrameworkCore.PostgreSQL.

Ensure Microsoft.EntityFrameworkCore.Design remains if needed for migrations.

Refactor AddDatabase (or equivalent) to Postgres

Open the extension that currently configures the DbContext (AddDatabase(contentRootPath) or similar).

Replace UseSqlite(...) with something like:

csharp
Copy code
options.UseNpgsql(configuration.GetConnectionString("TubesterDb"));
Remove any SQLite-specific path building (e.g. Path.Combine(contentRootPath, "App_Data", "Tubester.db")).

Make sure TubesterDb context works with Postgres types (e.g., timestamps, enums, guids, etc.).

If there are multiple entry points (API + Worker) that each call AddDatabase, confirm they all pass a configuration that
includes a Postgres connection string.

Set up Postgres connection strings in configuration

In appsettings.Development.json (for API and Worker), add a ConnectionStrings section if not present:

json
Copy code
"ConnectionStrings": {
"TubesterDb": "Host=localhost;Port=5432;Database=Tubester;Username=Tubester;Password=Tubester"
}
In production / generic appsettings.json, either:

Leave the connection string empty and rely on environment variables, or

Add a placeholder Postgres connection string.

Ensure both API and Worker use the same connection string name (e.g. "TubesterDb").

Recreate EF Core migrations for Postgres

Because the old migrations were generated for SQLite, it’s safer to:

Delete existing migrations from the Migrations folder (only if we’re okay with resetting DB schema for now).

Run:

bash
Copy code
dotnet ef migrations add InitialPostgresMigration -p Tubester.Persistence -s Tubester.Api
(adjust -p and -s to match your project setup).

Then:

bash
Copy code
dotnet ef database update -p Tubester.Persistence -s Tubester.Api
Verify:

The database schema is created in Postgres.

Types look correct (e.g., text, timestamp with time zone, uuid, etc.).

If we can’t delete old migrations (e.g., production already uses them), mark in the TODOs how to create a new
Postgres-only migration path (but for now, assume we’re okay with a clean slate).

Update tests

For integration tests that used SQLite:

Either:

Switch them to use Postgres via a test connection string (e.g., a dedicated test DB or ephemeral Docker instance).

Or, temporarily disable them until a proper test Postgres is wired.

For tests that only care about in-memory behavior:

Keep using UseInMemoryDatabase, not UseSqlite.

Remove any SQLite-specific test helpers (e.g., those building .db files).

Clean up SQLite remnants

Remove:

Any .db file creation logic.

Any SQLite-only annotations or workarounds (e.g., HasConversion hacks that were only for SQLite).

Any config sections like "Sqlite" that are now unused.

Do a full-text search for Sqlite and sqlite and ensure nothing is left except maybe in docs or comments that you want to
keep.

Verify runtime behavior with Postgres

Run the API against Docker Postgres:

Make sure migrations apply.

Basic operations (e.g., user login, token storage, channel sync enqueue) hit Postgres correctly.

Run the Worker pointing at the same Postgres instance:

Ensure background jobs that touch the DB run without errors.

Confirm no errors related to provider differences (e.g., date/time, unique constraints, etc.).

(Optional) Add a Docker Compose for local Dev

If not present, create a simple docker-compose.yml defining Postgres + maybe Adminer/pgAdmin for local inspections.

Document required environment variables and connection string usage.

Deliverables
Updated code:

AddDatabase (or similar) using UseNpgsql and Postgres connection strings.

Tubester.Persistence migrations regenerated for Postgres.

EF Core provider references switched to Npgsql.EntityFrameworkCore.PostgreSQL.

Config:

appsettings.Development.json with a TubesterDb Postgres connection string.

All SQLite-specific config removed or clearly obsolete.

A short summary of:

Where SQLite was removed from.

How to run migrations and start the app against Postgres.

Any test changes required (e.g., integration tests DB setup).

Make the diff as minimal and coherent as possible, but by the end of the refactor there should be no functional
dependency on SQLite anywhere in the solution.

makefile
Copy code
::contentReference[oaicite:0]{index=0}