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
    /// <summary>
    /// Приводит строку к формату, понятному Npgsql, и проверяет её разбор.
    /// Бросает InvalidOperationException с понятным текстом, если строка битая, —
    /// падать нужно при старте, а не при первом обращении к базе из UI.
    /// </summary>
    public static string Normalize(string connectionString)
    {
        // При вставке в поля дашбордов часто прилипают пробелы, перевод строки
        // или кавычки от копирования — из-за них строка не опознаётся.
        var value = connectionString.Trim().Trim('"', '\'');

        if (value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            value = FromUri(value);
        }

        try
        {
            _ = new NpgsqlConnectionStringBuilder(value);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(
                "Строка подключения ConnectionStrings:Default не разобрана. Ожидается формат " +
                "«Host=…;Port=5432;Database=postgres;Username=…;Password=…;SslMode=Require» " +
                "или URI «postgresql://user:password@host:port/database». " +
                "На хостинге это переменная окружения ConnectionStrings__Default.", ex);
        }

        return value;
    }

    private static string FromUri(string uriString)
    {
        var uri = new Uri(uriString);
        var userInfo = uri.UserInfo.Split(':', 2);
        var database = uri.AbsolutePath.Trim('/');

        return new NpgsqlConnectionStringBuilder
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
        }.ConnectionString;
    }
}
