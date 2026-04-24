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
                Message = "AJANLARIM_MYSQL .env icinde tanimli degil."
            };
        }

        try
        {
            await using var connection = new MySqlConnection(ConnectionStringService.NormalizeMySql(connectionString));
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
                Message = $"ajanlarim baglantisi tamam. Tablo sayisi: {count}"
            };
        }
        catch (Exception exception)
        {
            return new DatabaseHealthResult
            {
                IsConfigured = true,
                IsSuccess = false,
                Message = $"ajanlarim baglanti hatasi: {exception.Message}"
            };
        }
    }
}
