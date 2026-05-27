using MySqlConnector;

namespace Opstrax.Api.Infrastructure;

public sealed class Database
{
    private readonly string _connectionString;

    public Database(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Default");
    }

    public async Task<MySqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public async Task<List<T>> QueryAsync<T>(
        string sql,
        Func<MySqlDataReader, T> map,
        Action<MySqlCommand>? bind = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        bind?.Invoke(command);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<T>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(map(reader));
        }
        return rows;
    }

    public async Task<T?> QuerySingleAsync<T>(
        string sql,
        Func<MySqlDataReader, T> map,
        Action<MySqlCommand>? bind = null,
        CancellationToken cancellationToken = default)
    {
        var rows = await QueryAsync(sql, map, bind, cancellationToken);
        return rows.FirstOrDefault();
    }

    public async Task<int> ExecuteAsync(
        string sql,
        Action<MySqlCommand>? bind = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        bind?.Invoke(command);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<T> ScalarAsync<T>(
        string sql,
        Action<MySqlCommand>? bind = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection);
        bind?.Invoke(command);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is null || value is DBNull)
        {
            return default!;
        }
        return (T)Convert.ChangeType(value, typeof(T));
    }
}
