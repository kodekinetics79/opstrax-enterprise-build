using Npgsql;

namespace Opstrax.Api.Data;

public sealed class Database(IConfiguration configuration)
{
    // Credentials are environment-only — never hard-coded in appsettings.json.
    // Resolution order: ConnectionStrings:DefaultConnection (incl. the
    // ConnectionStrings__DefaultConnection env var used by docker-compose) →
    // the PG_CONNECTION env var (used by Render / .env). Fails fast if neither is set.
    private readonly string _connectionString =
        Coalesce(
            configuration.GetConnectionString("DefaultConnection"),
            Environment.GetEnvironmentVariable("PG_CONNECTION"))
        ?? throw new InvalidOperationException(
            "Database connection string is not configured. Set ConnectionStrings__DefaultConnection or PG_CONNECTION.");

    private static string? Coalesce(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken ct = default)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    public async Task<List<Dictionary<string, object?>>> QueryAsync(string sql, Action<NpgsqlCommand>? bind = null, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        bind?.Invoke(command);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[ToCamel(reader.GetName(i))] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }
            rows.Add(row);
        }
        return rows;
    }

    public async Task<Dictionary<string, object?>?> QuerySingleAsync(string sql, Action<NpgsqlCommand>? bind = null, CancellationToken ct = default)
        => (await QueryAsync(sql, bind, ct)).FirstOrDefault();

    public async Task<long> ScalarLongAsync(string sql, Action<NpgsqlCommand>? bind = null, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        bind?.Invoke(command);
        var value = await command.ExecuteScalarAsync(ct);
        return value is null or DBNull ? 0 : Convert.ToInt64(value);
    }

    public async Task<decimal?> ScalarDecimalAsync(string sql, Action<NpgsqlCommand>? bind = null, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        bind?.Invoke(command);
        var value = await command.ExecuteScalarAsync(ct);
        return value is null or DBNull ? null : Convert.ToDecimal(value);
    }

    public async Task<int> ExecuteAsync(string sql, Action<NpgsqlCommand>? bind = null, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, connection);
        bind?.Invoke(command);
        return await command.ExecuteNonQueryAsync(ct);
    }

    // Appends RETURNING id if not already present, then returns the inserted id.
    public async Task<long> InsertAsync(string sql, Action<NpgsqlCommand>? bind = null, CancellationToken ct = default)
    {
        var pgSql = sql.TrimEnd(';', ' ', '\n', '\r');
        if (!pgSql.Contains("RETURNING", StringComparison.OrdinalIgnoreCase))
            pgSql += " RETURNING id";

        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand(pgSql, connection);
        bind?.Invoke(command);
        var value = await command.ExecuteScalarAsync(ct);
        return value is null or DBNull ? 0 : Convert.ToInt64(value);
    }

    private static string ToCamel(string value)
    {
        var parts = value.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return value;
        return parts[0].ToLowerInvariant() + string.Concat(parts.Skip(1).Select(p => char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));
    }
}
