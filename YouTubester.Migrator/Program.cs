using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using YouTubester.Persistence;

var cancellationTokenSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationTokenSource.Cancel();
};

var path = Directory.GetCurrentDirectory();

var configurationBuilder = new ConfigurationBuilder()
    .SetBasePath(path)
    .AddJsonFile("appsettings.json", true)
    .AddEnvironmentVariables();

var configuration = configurationBuilder.Build();

var sourceSqliteConnectionString =
    Environment.GetEnvironmentVariable("YOUTUBESTER_MIGRATOR_SOURCE")
    ?? configuration.GetConnectionString("SourceSqlite")
    ?? throw new InvalidOperationException("Source (SQLite) connection string not configured.");

var destinationPostgresConnectionString =
    Environment.GetEnvironmentVariable("YOUTUBESTER_MIGRATOR_DEST")
    ?? configuration.GetConnectionString("DestinationPostgres")
    ?? throw new InvalidOperationException("Destination (Postgres) connection string not configured.");

Console.WriteLine("This will copy data from SQLite to Postgres.");
Console.WriteLine($"Source (SQLite): {sourceSqliteConnectionString}");
Console.WriteLine($"Destination (Postgres): {destinationPostgresConnectionString}");
Console.Write("Are you sure you want to continue? (y/N): ");

var confirmationAnswer = Console.ReadLine();
if (!string.Equals(confirmationAnswer, "y", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("Aborted.");
    return;
}

var sourceDbContextOptions = new DbContextOptionsBuilder<YouTubesterDb>()
    .UseSqlite(sourceSqliteConnectionString)
    .Options;

var destinationDbContextOptions = new DbContextOptionsBuilder<YouTubesterDb>()
    .UseNpgsql(destinationPostgresConnectionString)
    .Options;

await using var sourceDbContext = new YouTubesterDb(sourceDbContextOptions);
await using var destinationDbContext = new YouTubesterDb(destinationDbContextOptions);

// Keeps insertion fast for large migrations.
destinationDbContext.ChangeTracker.AutoDetectChangesEnabled = false;

Console.WriteLine("Starting migration...");

// FK-safe order for current YouTubesterDb model:
// Users -> Channels -> Videos -> Playlists -> VideoPlaylists -> Replies -> UserTokens
await CopyTableAsync(
    sourceDbContext.Users.AsNoTracking().AsAsyncEnumerable(),
    destinationDbContext.Users,
    destinationDbContext,
    nameof(destinationDbContext.Users),
    cancellationTokenSource.Token);

await CopyTableAsync(
    sourceDbContext.Channels.AsNoTracking().AsAsyncEnumerable(),
    destinationDbContext.Channels,
    destinationDbContext,
    nameof(destinationDbContext.Channels),
    cancellationTokenSource.Token);

await CopyTableAsync(
    sourceDbContext.Videos.AsNoTracking().AsAsyncEnumerable(),
    destinationDbContext.Videos,
    destinationDbContext,
    nameof(destinationDbContext.Videos),
    cancellationTokenSource.Token);

await CopyTableAsync(
    sourceDbContext.Playlists.AsNoTracking().AsAsyncEnumerable(),
    destinationDbContext.Playlists,
    destinationDbContext,
    nameof(destinationDbContext.Playlists),
    cancellationTokenSource.Token);

await CopyTableAsync(
    sourceDbContext.VideoPlaylists.AsNoTracking().AsAsyncEnumerable(),
    destinationDbContext.VideoPlaylists,
    destinationDbContext,
    nameof(destinationDbContext.VideoPlaylists),
    cancellationTokenSource.Token);

await CopyTableAsync(
    sourceDbContext.Replies.AsNoTracking().AsAsyncEnumerable(),
    destinationDbContext.Replies,
    destinationDbContext,
    nameof(destinationDbContext.Replies),
    cancellationTokenSource.Token);

await CopyTableAsync(
    sourceDbContext.UserTokens.AsNoTracking().AsAsyncEnumerable(),
    destinationDbContext.UserTokens,
    destinationDbContext,
    nameof(destinationDbContext.UserTokens),
    cancellationTokenSource.Token);

Console.WriteLine("Migration completed.");

static async Task CopyTableAsync<TEntity>(
    IAsyncEnumerable<TEntity> sourceEntities,
    DbSet<TEntity> destinationEntities,
    YouTubesterDb destinationDbContext,
    string entitySetName,
    CancellationToken cancellationToken)
    where TEntity : class
{
    const int batchSize = 1000;

    var copiedEntityCount = 0;
    var batchEntities = new List<TEntity>(batchSize);

    await foreach (var entity in sourceEntities.WithCancellation(cancellationToken))
    {
        batchEntities.Add(entity);

        if (batchEntities.Count < batchSize)
        {
            continue;
        }

        copiedEntityCount = await FlushBatchAsync(
            destinationEntities,
            destinationDbContext,
            entitySetName,
            batchEntities,
            copiedEntityCount,
            cancellationToken);
    }

    if (batchEntities.Count > 0)
    {
        copiedEntityCount = await FlushBatchAsync(
            destinationEntities,
            destinationDbContext,
            entitySetName,
            batchEntities,
            copiedEntityCount,
            cancellationToken);
    }

    Console.WriteLine($"[{entitySetName}] Finished. Copied {copiedEntityCount} rows.");
}

static async Task<int> FlushBatchAsync<TEntity>(
    DbSet<TEntity> destinationEntities,
    YouTubesterDb destinationDbContext,
    string entitySetName,
    List<TEntity> batchEntities,
    int copiedEntityCount,
    CancellationToken cancellationToken)
    where TEntity : class
{
    NormalizeDateTimeOffsets(batchEntities);

    await destinationEntities.AddRangeAsync(batchEntities, cancellationToken);
    await destinationDbContext.SaveChangesAsync(cancellationToken);

    // Keeps memory usage flat by detaching inserted entities.
    destinationDbContext.ChangeTracker.Clear();

    copiedEntityCount += batchEntities.Count;
    Console.WriteLine($"[{entitySetName}] Copied {copiedEntityCount} rows...");

    batchEntities.Clear();
    return copiedEntityCount;
}

static void NormalizeDateTimeOffsets<TEntity>(IEnumerable<TEntity> entities)
{
    // Find all writable DateTimeOffset / DateTimeOffset? properties on TEntity
    var props = typeof(TEntity)
        .GetProperties()
        .Where(p => p.CanRead && p.CanWrite &&
                    (p.PropertyType == typeof(DateTimeOffset) ||
                     p.PropertyType == typeof(DateTimeOffset?)))
        .ToArray();

    if (props.Length == 0)
    {
        return;
    }

    foreach (var entity in entities)
    {
        foreach (var prop in props)
        {
            var value = prop.GetValue(entity);
            switch (value)
            {
                case DateTimeOffset dto:
                    prop.SetValue(entity, dto.ToUniversalTime());
                    break;
            }
        }
    }
}