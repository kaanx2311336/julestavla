using MySqlConnector;
using TavlaJules.App.Models;

namespace TavlaJules.App.Services;

public sealed class DatabaseHealthService
{
    public async Task<DatabaseHealthResult> TestAsync(string? connectionString, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new DatabaseHealthResult
            {
                IsConfigured = false,
                Message = "TAVLA_ONLINE_MYSQL .env icinde tanimli degil."
            };
        }

        try
        {
            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT COUNT(*)
                FROM information_schema.tables
                WHERE table_schema = DATABASE();
                """;

            var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            return new DatabaseHealthResult
            {
                IsConfigured = true,
                IsSuccess = true,
                TableCount = count,
                Message = $"tavla_online baglantisi tamam. Tablo sayisi: {count}"
            };
        }
        catch (Exception exception)
        {
            return new DatabaseHealthResult
            {
                IsConfigured = true,
                IsSuccess = false,
                Message = $"tavla_online baglanti hatasi: {exception.Message}"
            };
        }
    }
}
