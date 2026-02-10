using System.Data;
using System.Data.Common;

namespace Tubester.IntegrationTests.TestHost;

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

            var tables = new List<(string Schema, string Table)>();

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                                      SELECT schemaname, tablename
                                      FROM pg_tables
                                      WHERE schemaname IN ('public', 'analytics')
                                        AND NOT (schemaname = 'public' AND tablename = '__EFMigrationsHistory');
                                      """;

                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    tables.Add((reader.GetString(0), reader.GetString(1)));
                }
            }

            if (tables.Count > 0)
            {
                var qualified = tables.Select(t => $"\"{t.Schema}\".\"{t.Table}\"");
                var truncateStatement =
                    $"TRUNCATE TABLE {string.Join(", ", qualified)} RESTART IDENTITY CASCADE;";

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