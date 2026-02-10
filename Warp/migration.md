You are helping me add a one-off **data migration tool** project to my Tubester solution that copies all data from
the old **SQLite** DB into the new **PostgreSQL** DB using EF Core.

## Context

- Solution name: **Tubester**
- Main projects:
    - `Tubester.Api` (ASP.NET Core API)
    - `Tubester.Worker` (background worker)
    - `Tubester.Persistence` (EF Core DbContext + entities)
- DbContext: `Tubester.Persistence.TubesterDb`

We have just refactored the app to use **PostgreSQL** everywhere (API + Worker) with Npgsql.  
Previously, **SQLite** was used as the primary DB. The old SQLite file still contains real data that I want to keep.

### Current DB situation

- **SQLite**:
    - Was used by previous versions of the app.
    - stored in `.data/Tubester.db`
- **Postgres (new)**:
    - Runs in Docker (local dev) on `localhost:5432`.
    - Has a DB for dev: `Tubester`.
    - Has a DB for tests: `Tubester_test`.
    - Schema is already created via **EF Core migrations** for Postgres (i.e., `dotnet ef database update` has been
      run).

I now want a **small, separate console project** that:

- Opens `TubesterDb` pointing to **SQLite** as *source*.
- Opens `TubesterDb` pointing to **Postgres** as *destination*.
- Copies data **table-by-table**, in FK-safe order.
- Is used **once** (or rarely) to migrate data, then can be ignored.

## Requirements for the migrator

1. **New project: `Tubester.Migrator`**

    - Type: `.NET` console app, target `net10.0` (same as rest of solution).
    - Add a project reference to `Tubester.Persistence` so we can reuse `TubesterDb` and all entity types.
    - Add NuGet packages:
        - `Microsoft.EntityFrameworkCore.Sqlite`
        - `Npgsql.EntityFrameworkCore.PostgreSQL`

2. **Connection strings**

   The migrator should obtain connection strings from **configuration + optional environment variables**:

    - Configuration:
        - Add `appsettings.json` to `Tubester.Migrator` with something like:

          ```json
          {
            "ConnectionStrings": {
              "SourceSqlite": "Data Source=./data/Tubester.db",
              "DestinationPostgres": "Host=localhost;Port=5432;Database=Tubester;Username=app;Password=devpassword"
            }
          }
          ```

          (The exact SQLite path should match whatever the old app used; inspect existing code/config to confirm.)

    - Environment overrides (optional but nice for CI/prod):
        - If `Tubester_MIGRATOR_SOURCE` is set, it overrides `ConnectionStrings:SourceSqlite`.
        - If `Tubester_MIGRATOR_DEST` is set, it overrides `ConnectionStrings:DestinationPostgres`.

    - In `Program.cs`, read config like:

      ```csharp
      var builder = new ConfigurationBuilder()
          .SetBasePath(AppContext.BaseDirectory)
          .AddJsonFile("appsettings.json", optional: true)
          .AddEnvironmentVariables();
 
      var configuration = builder.Build();
 
      var sqliteCs = Environment.GetEnvironmentVariable("Tubester_MIGRATOR_SOURCE")
                     ?? configuration.GetConnectionString("SourceSqlite")
                     ?? throw new InvalidOperationException("Source (SQLite) connection string not configured.");
 
      var postgresCs = Environment.GetEnvironmentVariable("Tubester_MIGRATOR_DEST")
                       ?? configuration.GetConnectionString("DestinationPostgres")
                       ?? throw new InvalidOperationException("Destination (Postgres) connection string not configured.");
      ```

3. **Use `TubesterDb` with two different providers**

   In `Program.cs`:

    - Build two sets of `DbContextOptions<TubesterDb>`:

      ```csharp
      var sqliteOptions = new DbContextOptionsBuilder<TubesterDb>()
          .UseSqlite(sqliteCs)
          .Options;
 
      var pgOptions = new DbContextOptionsBuilder<TubesterDb>()
          .UseNpgsql(postgresCs)
          .Options;
      ```

    - Create `source` and `target` contexts:

      ```csharp
      using var source = new TubesterDb(sqliteOptions);
      using var target = new TubesterDb(pgOptions);
 
      target.ChangeTracker.AutoDetectChangesEnabled = false;
      ```

4. **Copy data table-by-table in FK-safe order**

   Inspect `TubesterDb` to determine the DbSet order that respects foreign keys. Typical example (adjust to actual
   DbSets):

    - `Users`
    - `Channels`
    - `Playlists`
    - `Videos`
    - `VideoPlaylists`
    - `Replies`
    - `UserTokens`

   For each DbSet:

    - Read from the SQLite context with `AsNoTracking()`.
    - Copy in **batches** (e.g., 1000) into the Postgres context.
    - `SaveChangesAsync()` after each table (or after each batch) to reduce memory usage.

   Implement a generic helper:

   ```csharp
   static async Task CopyTableAsync<TEntity>(
       IQueryable<TEntity> sourceQuery,
       DbSet<TEntity> targetSet,
       string tableName)
       where TEntity : class
   {
       const int batchSize = 1000;
       var offset = 0;

       while (true)
       {
           var batch = await sourceQuery
               .Skip(offset)
               .Take(batchSize)
               .ToListAsync();

           if (batch.Count == 0)
               break;

           await targetSet.AddRangeAsync(batch);
           offset += batch.Count;

           Console.WriteLine($"[{tableName}] Copied {offset} rows...");
       }
   }

And then in Main:

Console.WriteLine("Starting migration...");

// Example order; adjust to actual DbSets
await CopyTableAsync(source.Users.AsNoTracking(), target.Users, nameof(target.Users));
await target.SaveChangesAsync();

await CopyTableAsync(source.Channels.AsNoTracking(), target.Channels, nameof(target.Channels));
await target.SaveChangesAsync();

await CopyTableAsync(source.Playlists.AsNoTracking(), target.Playlists, nameof(target.Playlists));
await target.SaveChangesAsync();

await CopyTableAsync(source.Videos.AsNoTracking(), target.Videos, nameof(target.Videos));
await target.SaveChangesAsync();

await CopyTableAsync(source.Replies.AsNoTracking(), target.Replies, nameof(target.Replies));
await target.SaveChangesAsync();

// ... and so on for any other DbSets.

Console.WriteLine("Migration completed.");

Assumptions / handling IDs and timestamps

PKs are mostly string IDs (e.g., YouTube IDs) or GUIDs, not auto-increment ints; inserting them as-is into Postgres is
fine.

Date/time:

If you’ve already normalized DateTime/DateTimeOffset to UTC for Postgres, there should be no conversion issues.

If needed, you can add minor adaptations (e.g., Select to tweak values before inserting), but avoid overcomplicating
unless tests show a problem.

The Postgres DB schema must already exist (via EF migrations) before running the migrator. The tool should not run
migrations itself, only move data.

Behavior and safety

The migrator is intended to be run when:

Postgres DB is empty or at least doesn’t conflict with existing IDs.

The old SQLite DB is in a stable state (no concurrent writes).

If needed, you can add a confirmation prompt at the start:

Console.WriteLine("This will copy data from SQLite to Postgres.");
Console.Write("Are you sure you want to continue? (y/N): ");
var answer = Console.ReadLine();
if (!string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase))
{
Console.WriteLine("Aborted.");
return;
}

Add a simple README section

In the repo (or in a README.migrator.md), document:

How to run the migrator:

dotnet run -p Tubester.Migrator

Which config it uses (appsettings.json + env overrides).

That Postgres schema must be created first:

dotnet ef database update -p Tubester.Persistence -s Tubester.Api

Tasks for you

Create the new project Tubester.Migrator and wire it into the solution.

Add the project reference to Tubester.Persistence and required EF Core providers.

Implement appsettings.json and Program.cs as described, including:

Config reading,

Source/destination DbContext setup,

Generic CopyTableAsync helper,

Table-by-table copy in FK-safe order.

Make the code compile and be ready to run with:

dotnet run -p Tubester.Migrator

assuming there is a SQLite DB file at the configured path and a Postgres DB with schema already created.

Please generate the full Program.cs and an example appsettings.json for Tubester.Migrator, plus any necessary .csproj
modifications.