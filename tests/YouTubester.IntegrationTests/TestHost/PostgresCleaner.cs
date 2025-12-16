using System.Data;
using System.Data.Common;

namespace YouTubester.IntegrationTests.TestHost;

public static class PostgresCleaner
{
    public static async Task CleanAsync(DbConnection connection)
    {
        var wasOpen = connection.State == ConnectionState.Open;
        if (!wasOpen)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var transaction = await connection.BeginTransactionAsync();

            var tableNames = new List<string>();
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
SELECT tablename
FROM pg_tables
WHERE schemaname = 'public'
  AND tablename <> '__EFMigrationsHistory';
""";

                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    tableNames.Add(reader.GetString(0));
                }
            }

            if (tableNames.Count > 0)
            {
                var quotedTableNames = tableNames.Select(tableName => $"\"{tableName}\"");
                var truncateStatement = $"TRUNCATE TABLE {string.Join(", ", quotedTableNames)} RESTART IDENTITY CASCADE;";

                await using var truncateCommand = connection.CreateCommand();
                truncateCommand.Transaction = transaction;
                truncateCommand.CommandText = truncateStatement;
                await truncateCommand.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        finally
        {
            if (!wasOpen)
            {
                await connection.CloseAsync();
            }
        }
    }
}
