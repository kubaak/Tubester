# Tubester.Migrator

One-off console tool to copy all data from the legacy SQLite database into the PostgreSQL database.

## Prerequisites

1. The destination PostgreSQL database must already exist and have the schema created via EF Core migrations.

   Example (from repo root):

   ```powershell
   dotnet ef database update -p Tubester.Persistence -s Tubester.Api
   ```

2. The source SQLite database file must exist (default: `./.data/Tubester.db`).

## Configuration

Configuration sources (highest precedence first):

1. Environment variables
2. `Tubester.Migrator/appsettings.json`

Environment variable overrides:

- `Tubester_MIGRATOR_SOURCE` overrides `ConnectionStrings:SourceSqlite`
- `Tubester_MIGRATOR_DEST` overrides `ConnectionStrings:DestinationPostgres`

## Run

From the repo root:

```powershell
dotnet run -p Tubester.Migrator
```

Example with explicit connection strings:

```powershell
$env:Tubester_MIGRATOR_SOURCE = "Data Source=./.data/Tubester.db"
$env:Tubester_MIGRATOR_DEST = "Host=localhost;Port=5432;Database=Tubester;Username=app;Password=devpassword"

dotnet run -p Tubester.Migrator
```

## Safety

Run this when the PostgreSQL database is empty (or at least does not conflict on IDs) and when the SQLite database is not being written to.
