using Npgsql;

namespace Kneset.Infrastructure.Data;

/// <summary>
/// Supabase (и большинство хостингов) показывают строку подключения в виде URI
/// «postgresql://user:password@host:port/database», а Npgsql принимает только формат
/// «ключ=значение» и на URI падает с ArgumentException. Приводим URI к нужному виду,
/// строку в формате Npgsql отдаём как есть.
/// </summary>
public static class PostgresConnectionString
{
    public static string Normalize(string connectionString)
    {
        if (!connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return connectionString;
        }

        var uri = new Uri(connectionString);
        var userInfo = uri.UserInfo.Split(':', 2);
        var database = uri.AbsolutePath.Trim('/');

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = database.Length > 0 ? database : "postgres",
            // Пароль в URI приходит percent-encoded: спецсимволы нужно раскодировать.
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
            // Supabase принимает подключения только по TLS. Параметры из query-строки
            // URI (?sslmode=...) намеренно не разбираем — задавать их через URI незачем.
            SslMode = SslMode.Require
        };

        return builder.ConnectionString;
    }
}
