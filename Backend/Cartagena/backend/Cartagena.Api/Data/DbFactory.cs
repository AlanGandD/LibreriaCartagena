using MySqlConnector;

namespace Cartagena.Api.Data;

public sealed class DbFactory(IConfiguration config)
{
    public async Task<MySqlConnection> OpenAsync(CancellationToken ct = default)
    {
        var cs = new MySqlConnectionStringBuilder
        {
            Server = config["DB_HOST"] ?? "mysql",
            Port = uint.Parse(config["DB_PORT"] ?? "3306"),
            Database = config["DB_NAME"] ?? "tienda_online",
            UserID = config["DB_USER"] ?? throw new InvalidOperationException("Falta DB_USER"),
            Password = config["DB_PASSWORD"] ?? throw new InvalidOperationException("Falta DB_PASSWORD"),
            CharacterSet = "utf8mb4",
            ConnectionTimeout = 10,
            DefaultCommandTimeout = 20,
            SslMode = MySqlSslMode.Preferred,
            AllowUserVariables = false
        };
        var conn = new MySqlConnection(cs.ConnectionString);
        await conn.OpenAsync(ct);
        return conn;
    }
}
