using MySqlConnector;

namespace Opstrax.Api.Data;

public sealed class Database(IConfiguration configuration)
{
    private readonly string _connectionString = configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Missing ConnectionStrings:DefaultConnection");

    public async Task<MySqlConnection> OpenAsync(CancellationToken ct = default)
    {
        var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    public async Task<List<Dictionary<string, object?>>> QueryAsync(string sql, Action<MySqlCommand>? bind = null, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = new MySqlCommand(sql, connection);
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

    public async Task<Dictionary<string, object?>?> QuerySingleAsync(string sql, Action<MySqlCommand>? bind = null, CancellationToken ct = default)
        => (await QueryAsync(sql, bind, ct)).FirstOrDefault();

    public async Task<long> ScalarLongAsync(string sql, Action<MySqlCommand>? bind = null, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = new MySqlCommand(sql, connection);
        bind?.Invoke(command);
        var value = await command.ExecuteScalarAsync(ct);
        return value is null or DBNull ? 0 : Convert.ToInt64(value);
    }

    public async Task<int> ExecuteAsync(string sql, Action<MySqlCommand>? bind = null, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = new MySqlCommand(sql, connection);
        bind?.Invoke(command);
        return await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<long> InsertAsync(string sql, Action<MySqlCommand>? bind = null, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = new MySqlCommand($"{sql}; SELECT LAST_INSERT_ID();", connection);
        bind?.Invoke(command);
        var value = await command.ExecuteScalarAsync(ct);
        return Convert.ToInt64(value);
    }

    private static string ToCamel(string value)
    {
        var parts = value.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return value;
        return parts[0].ToLowerInvariant() + string.Concat(parts.Skip(1).Select(p => char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));
    }
}
