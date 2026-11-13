using Npgsql;

namespace TempStore.Data;

public static class DatabaseConfiguration
{
    public static string ConnectionString(string value)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException();
            }

            if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
                && !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            {
                var connection = new NpgsqlConnectionStringBuilder(value);
                if (string.IsNullOrWhiteSpace(connection.Host))
                {
                    throw new ArgumentException();
                }

                return connection.ConnectionString;
            }

            var uri = new Uri(value, UriKind.Absolute);
            if (!uri.IsWellFormedOriginalString() || string.IsNullOrWhiteSpace(uri.Host)
                || uri.AbsolutePath.Length <= 1 || uri.Fragment.Length > 0 || uri.Port == 0)
            {
                throw new ArgumentException();
            }

            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = uri.Port < 0 ? 5432 : uri.Port,
                Database = Uri.UnescapeDataString(uri.AbsolutePath[1..])
            };
            if (uri.UserInfo.Length > 0)
            {
                var credentials = uri.UserInfo.Split(':', 2);
                builder.Username = Uri.UnescapeDataString(credentials[0]);
                if (credentials.Length == 2)
                {
                    builder.Password = Uri.UnescapeDataString(credentials[1]);
                }
            }

            var hasSslMode = false;
            foreach (var parameter in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = parameter.Split('=', 2);
                if (pair.Length != 2 || hasSslMode
                    || !Uri.UnescapeDataString(pair[0]).Equals("sslmode", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException();
                }

                builder.SslMode = Uri.UnescapeDataString(pair[1]).ToLowerInvariant() switch
                {
                    "disable" => SslMode.Disable,
                    "allow" => SslMode.Allow,
                    "prefer" => SslMode.Prefer,
                    "require" => SslMode.Require,
                    "verify-ca" => SslMode.VerifyCA,
                    "verify-full" => SslMode.VerifyFull,
                    _ => throw new ArgumentException()
                };
                hasSslMode = true;
            }

            return builder.ConnectionString;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
        {
            throw new ArgumentException("Некорректный DATABASE_URL: укажите адрес PostgreSQL или строку подключения Npgsql.");
        }
    }
}
