using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Npgsql;
using NpgsqlTypes;
using Opstrax.Api.Controllers;

namespace Opstrax.Tests;

public sealed class MaintenanceDateBindingTests
{
    [Theory]
    [InlineData("2026-09-21")]
    [InlineData("2028-02-29")]
    public void BrowserJsonDateBindsAsPostgresDate(string date)
    {
        var body = JsonSerializer.Deserialize<Dictionary<string, object?>>($"{{\"dueDate\":\"{date}\"}}")!;
        using var command = new NpgsqlCommand();
        Bind(command, body);
        var parameter = command.Parameters["@dueDate"];
        Assert.Equal(NpgsqlDbType.Date, parameter.NpgsqlDbType);
        Assert.Equal(DateOnly.Parse(date), Assert.IsType<DateOnly>(parameter.Value));
    }

    [Fact]
    public void OdometerOnlyMaintenanceKeepsATypedNullDate()
    {
        using var command = new NpgsqlCommand();
        Bind(command, new Dictionary<string, object?> { ["dueOdometer"] = 5000 });
        Assert.Equal(NpgsqlDbType.Date, command.Parameters["@dueDate"].NpgsqlDbType);
        Assert.Equal(DBNull.Value, command.Parameters["@dueDate"].Value);
    }

    [Theory]
    [InlineData("CreateMaintenance", "dueDate", "not-a-date")]
    [InlineData("CreateMaintenance", "dueDate", "2026-02-30")]
    [InlineData("UpdateMaintenance", "dueDate", "not-a-date")]
    [InlineData("MaintenanceSchedule", "scheduledDate", "not-a-date")]
    [InlineData("MaintenanceDefer", "dueDate", "not-a-date")]
    public async Task InvalidDatesAreRejectedBeforeDatabaseWork(string handler, string key, string value)
    {
        var body = new Dictionary<string, object?> { ["vehicleId"] = 1L, ["serviceType"] = "Inspection", [key] = value };
        object?[] arguments = handler == "CreateMaintenance"
            ? [new DefaultHttpContext(), body, null, null, CancellationToken.None]
            : [new DefaultHttpContext(), 1L, body, null, null, CancellationToken.None];
        var method = typeof(EndpointMappings).GetMethod(handler, BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = await (Task<IResult>)method.Invoke(null, arguments)!;
        Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    internal static void Bind(NpgsqlCommand command, Dictionary<string, object?> body)
    {
        typeof(EndpointMappings).GetMethod("BindMaintenance", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [command, body]);
    }
}

[Trait("Category", "Integration")]
public sealed class MaintenanceDateBindingPostgresTests
{
    [Fact]
    public async Task BrowserDatePersistsInADateColumnWithoutSqlCasts()
    {
        var configured = Environment.GetEnvironmentVariable("OPSTRAX_TEST_DB");
        Assert.False(string.IsNullOrWhiteSpace(configured), "Select a dedicated disposable local PostgreSQL database.");
        var settings = new NpgsqlConnectionStringBuilder(configured!);
        Assert.Contains(settings.Host, new[] { "127.0.0.1", "localhost" });
        Assert.StartsWith("opstrax_", settings.Database!);
        await using var connection = new NpgsqlConnection(settings.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var create = new NpgsqlCommand("CREATE TEMP TABLE maintenance_date_regression (due_date date)", connection, transaction);
        await create.ExecuteNonQueryAsync();
        var body = JsonSerializer.Deserialize<Dictionary<string, object?>>("{\"dueDate\":\"2026-09-21\"}")!;
        await using var insert = new NpgsqlCommand("INSERT INTO maintenance_date_regression (due_date) VALUES (@dueDate)", connection, transaction);
        MaintenanceDateBindingTests.Bind(insert, body);
        Assert.Equal(1, await insert.ExecuteNonQueryAsync());
        await using var read = new NpgsqlCommand("SELECT due_date FROM maintenance_date_regression", connection, transaction);
        Assert.Equal(new DateTime(2026, 9, 21), await read.ExecuteScalarAsync());
        await transaction.RollbackAsync();
    }
}
