# YouTubester.Migrator

One-off console tool to copy all data from the legacy SQLite database into the PostgreSQL database.

## Prerequisites

1. The destination PostgreSQL database must already exist and have the schema created via EF Core migrations.

   Example (from repo root):

   ```powershell
   dotnet ef database update -p YouTubester.Persistence -s YouTubester.Api
   ```

2. The source SQLite database file must exist (default: `./.data/youtubester.db`).

## Configuration

Configuration sources (highest precedence first):

1. Environment variables
2. `YouTubester.Migrator/appsettings.json`

Environment variable overrides:

- `YOUTUBESTER_MIGRATOR_SOURCE` overrides `ConnectionStrings:SourceSqlite`
- `YOUTUBESTER_MIGRATOR_DEST` overrides `ConnectionStrings:DestinationPostgres`

## Run

From the repo root:

```powershell
dotnet run -p YouTubester.Migrator
```

Example with explicit connection strings:

```powershell
$env:YOUTUBESTER_MIGRATOR_SOURCE = "Data Source=./.data/youtubester.db"
$env:YOUTUBESTER_MIGRATOR_DEST = "Host=localhost;Port=5432;Database=youtubester;Username=app;Password=devpassword"

dotnet run -p YouTubester.Migrator
```

## Safety

Run this when the PostgreSQL database is empty (or at least does not conflict on IDs) and when the SQLite database is not being written to.
