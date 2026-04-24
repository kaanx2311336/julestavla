using MySqlConnector;

namespace TavlaJules.App.Services;

public static class ConnectionStringService
{
    public static string NormalizeMySql(string connectionString)
    {
        if (!Uri.TryCreate(connectionString, UriKind.Absolute, out var uri) || uri.Scheme != "mysql")
        {
            return connectionString;
        }

        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new MySqlConnectionStringBuilder
        {
            Server = uri.Host,
            Port = (uint)(uri.Port > 0 ? uri.Port : 3306),
            Database = uri.AbsolutePath.TrimStart('/'),
            UserID = Uri.UnescapeDataString(userInfo.ElementAtOrDefault(0) ?? ""),
            Password = Uri.UnescapeDataString(userInfo.ElementAtOrDefault(1) ?? ""),
            SslMode = MySqlSslMode.Required
        };

        return builder.ConnectionString;
    }
}
