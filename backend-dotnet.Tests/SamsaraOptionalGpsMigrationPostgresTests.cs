using Npgsql;

namespace Opstrax.Tests;

[Trait("Category", "Integration")]
public sealed class SamsaraOptionalGpsMigrationPostgresTests
{
    [Fact]
    public async Task ExpansionIsIdempotentPreservesLegacyDefaultsAndStoresExplicitNulls()
    {
        var schema = $"synthetic_optional_gps_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(TestDb.ConnectionString);
        await connection.OpenAsync();
        async Task Execute(string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
        var tables = new[] { "location_events", "latest_vehicle_positions", "telemetry_live_asset_states" };
        try
        {
            await Execute($"CREATE SCHEMA {schema}; SET search_path TO {schema}; CREATE TABLE schema_migrations(version TEXT PRIMARY KEY, description TEXT);");
            foreach (var table in tables)
                await Execute($"CREATE TABLE {table}(id SERIAL PRIMARY KEY,speed_mph NUMERIC(6,2) NOT NULL DEFAULT 0,heading SMALLINT NOT NULL DEFAULT 0); INSERT INTO {table} DEFAULT VALUES;");
            var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../database/migrations/2026_09_02_stage98_optional_gps_measurements.sql"));
            var migration = await File.ReadAllTextAsync(path);
            await Execute(migration);
            await Execute(migration);
            await using (var command = new NpgsqlCommand("SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=@schema AND column_name IN ('speed_mph','heading') AND is_nullable='YES' AND column_default='0'", connection))
            {
                command.Parameters.AddWithValue("schema", schema);
                Assert.Equal(6L, await command.ExecuteScalarAsync());
            }
            foreach (var table in tables)
            {
                await Execute($"INSERT INTO {table}(speed_mph,heading) VALUES(NULL,NULL); INSERT INTO {table} DEFAULT VALUES;");
                await using var command = new NpgsqlCommand($"SELECT COUNT(*) FILTER (WHERE speed_mph=0 AND heading=0), COUNT(*) FILTER (WHERE speed_mph IS NULL AND heading IS NULL) FROM {table}", connection);
                await using var reader = await command.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(2L, reader.GetInt64(0));
                Assert.Equal(1L, reader.GetInt64(1));
            }
            await using var ledger = new NpgsqlCommand("SELECT COUNT(*) FROM schema_migrations", connection);
            Assert.Equal(1L, await ledger.ExecuteScalarAsync());
        }
        finally
        {
            // Only the unique schema created by this case is removed, never public.
            await Execute($"ROLLBACK; SET search_path TO public; DROP SCHEMA IF EXISTS {schema} CASCADE;");
        }
    }
}
