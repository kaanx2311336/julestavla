using System.Data;
using MySqlConnector;
using TavlaJules.App.Models;
using TavlaJules.Data.Repositories;

namespace TavlaJules.App.Services;

public sealed class MySqlConnectionFactory(ProjectSettings settings, EnvFileService envFileService) : IDbConnectionFactory
{
    public async Task<IDbConnection> CreateConnectionAsync()
    {
        var connectionString = envFileService.GetValue(settings.ProjectFolder, "TAVLA_ONLINE_MYSQL")
            ?? envFileService.GetValue(settings.ProjectFolder, "AJANLARIM_MYSQL");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("TAVLA_ONLINE_MYSQL veya AJANLARIM_MYSQL .env degeri bulunamadi.");
        }

        var normalizedConnectionString = ConnectionStringService.NormalizeMySql(connectionString);
        var connection = new MySqlConnection(normalizedConnectionString);
        await connection.OpenAsync();
        return connection;
    }
}
