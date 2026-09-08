using System.Net;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Opstrax.Api.Data;
using Opstrax.Api.Services.Connectors;

namespace Opstrax.Tests;

// Actual RunAsync/schema probe/connector/finalizer, with synthetic provider HTTP.
// Public tables below exist ONLY in a random, test-owned database, not opstrax_local.
// Minimal old-shape tables prove refusal boundaries, not a full migration, RLS,
// deployed application, provider connection, or production certification.
[Trait("Category", "Integration")]
public sealed class SamsaraOldSchemaRefusalPostgresTests
{
    private const string BeforeCursor = "before /+?=schema-check";
    private const string ProviderCursor = "must-not-consume";

    // Prefix masks cover zero through five nullable columns. Each single missing
    // column is also exercised so success cannot accidentally depend on a subset.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(15)]
    [InlineData(31)]
    [InlineData(47)]
    [InlineData(55)]
    [InlineData(59)]
    [InlineData(61)]
    [InlineData(62)]
    public async Task IncompleteNullableSchemaRefusesBeforeAnyWriteOrCursorConsumption(int nullableMask)
    {
        await using var fixture = await Fixture.CreateAsync(nullableMask);
        Assert.Equal(BitOperations.PopCount((uint)nullableMask), await fixture.NullableCountAsync());
        var schemaBefore = await fixture.SchemaFingerprintAsync();
        var rowBefore = await fixture.IntegrationVersionAsync();

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => fixture.RunPageAsync(allowPartial: true));
        Assert.Contains("requires the optional-measurement schema migration", exception.Message, StringComparison.Ordinal);
        Assert.Equal(rowBefore, await fixture.IntegrationVersionAsync());
        await fixture.AssertNoProviderWritesOrDdlAsync(schemaBefore);

        // The real connector must translate the refusal into zero committed pages;
        // the real finalizer may record Error, but must preserve the old checkpoint.
        var result = await fixture.RunConnectorAsync(allowPartial: true);
        Assert.False(result.Success);
        Assert.Contains("requires the optional-measurement schema migration", result.Message, StringComparison.Ordinal);
        Assert.Equal(0, result.Details!["pagesCommitted"]);
        Assert.Null(result.Details["nextCursor"]);
        Assert.False((bool)result.Details["boundedPartial"]!);
        Assert.Equal(1, await ConnectorOperationLease.CompleteSyncAsync(fixture.Db, fixture.Operation,
            result, result.Details["nextCursor"]?.ToString(), CancellationToken.None));
        await fixture.AssertFinalizedRefusalAsync();
        await fixture.AssertNoProviderWritesOrDdlAsync(schemaBefore);
        Assert.Equal(2, fixture.Requests.Count);
        Assert.All(fixture.Requests, AssertOriginalRequest);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(63)]
    public async Task DefaultOffRefusesEvenWhenAllSixColumnsAreNullable(int nullableMask)
    {
        await using var fixture = await Fixture.CreateAsync(nullableMask);
        var schemaBefore = await fixture.SchemaFingerprintAsync();
        var rowBefore = await fixture.IntegrationVersionAsync();
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => fixture.RunPageAsync(allowPartial: false));
        Assert.Contains("Partial GPS is paused", exception.Message, StringComparison.Ordinal);
        Assert.Equal(rowBefore, await fixture.IntegrationVersionAsync());
        var result = await fixture.RunConnectorAsync(allowPartial: false);
        Assert.False(result.Success);
        Assert.Contains("Partial GPS is paused", result.Message, StringComparison.Ordinal);
        Assert.Equal(0, result.Details!["pagesCommitted"]);
        Assert.Null(result.Details["nextCursor"]);
        Assert.Equal(1, await ConnectorOperationLease.CompleteSyncAsync(fixture.Db, fixture.Operation,
            result, null, CancellationToken.None));
        await fixture.AssertFinalizedRefusalAsync();
        await fixture.AssertNoProviderWritesOrDdlAsync(schemaBefore);
        Assert.Equal(2, fixture.Requests.Count);
        Assert.All(fixture.Requests, AssertOriginalRequest);
    }

    [Fact]
    public async Task SixNullableColumnsPassTheGuardAndReachTheControlledWriteBarrier()
    {
        await using var fixture = await Fixture.CreateAsync(63);
        Assert.Equal(6, await fixture.NullableCountAsync());
        var schemaBefore = await fixture.SchemaFingerprintAsync();
        var rowBefore = await fixture.IntegrationVersionAsync();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => fixture.RunPageAsync(allowPartial: true));
        Assert.Equal("P0001", exception.SqlState);
        Assert.Equal("synthetic provider write boundary", exception.MessageText);
        Assert.Equal(1, await fixture.AttemptCountAsync("provider_write_attempts"));
        Assert.Equal(0, await fixture.AttemptCountAsync("runtime_ddl_attempts"));
        Assert.Equal(schemaBefore, await fixture.SchemaFingerprintAsync());
        Assert.Equal(rowBefore, await fixture.IntegrationVersionAsync());
        AssertOriginalRequest(Assert.Single(fixture.Requests));
        // The positive control deliberately stops before device discovery persists.
        // It proves that old-schema failures above reached the intended gate, not
        // that the minimal fixture can replace application-schema integration tests.
        await fixture.AssertTablesEmptyAsync();
    }

    private static void AssertOriginalRequest(Uri uri)
    {
        Assert.Equal("/fleet/vehicles/stats/feed", uri.AbsolutePath);
        var query = QueryHelpers.ParseQuery(uri.Query);
        Assert.Equal("gps,engineStates,obdOdometerMeters", query["types"].ToString());
        Assert.Equal(BeforeCursor, query["after"].ToString());
        Assert.Equal(2, query.Count);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private const string DatabasePrefix = "opstrax_samsara_schema_";
        private readonly string _databaseName = DatabasePrefix + Guid.NewGuid().ToString("N");
        private readonly string _adminConnection;
        private readonly string _isolatedConnection;
        private bool _created;
        private long _databaseOid;
        public Database Db { get; }
        public ConnectorOperationContext Operation { get; }
        public List<Uri> Requests { get; } = [];

        private Fixture(NpgsqlConnectionStringBuilder admin)
        {
            admin.Pooling = false;
            _adminConnection = admin.ConnectionString;
            var isolated = new NpgsqlConnectionStringBuilder(_adminConnection)
            {
                Database = _databaseName, SearchPath = "public", Pooling = false,
                ApplicationName = "samsara-old-schema-test",
            };
            _isolatedConnection = isolated.ConnectionString;
            Db = new Database(Configuration(allowPartial: true));
            Operation = new ConnectorOperationContext(7, 11, 0, Guid.NewGuid(), "samsara", null, "Connected", true);
        }

        public static async Task<Fixture> CreateAsync(int nullableMask)
        {
            var configured = Environment.GetEnvironmentVariable("OPSTRAX_TEST_DB");
            if (string.IsNullOrWhiteSpace(configured))
                throw new InvalidOperationException("Set OPSTRAX_TEST_DB explicitly to the disposable localhost:5433/opstrax_local database.");
            var admin = new NpgsqlConnectionStringBuilder(configured);
            if (admin.Host is not ("127.0.0.1" or "localhost" or "::1") || admin.Port != 5433 || admin.Database != "opstrax_local")
                throw new InvalidOperationException("Old-schema tests refuse any base database except explicit localhost:5433/opstrax_local.");
            if (nullableMask is < 0 or > 63) throw new ArgumentOutOfRangeException(nameof(nullableMask));
            var fixture = new Fixture(admin);
            await using var connection = new NpgsqlConnection(fixture._adminConnection);
            await connection.OpenAsync();
            // Existing authority only: no CREATE ROLE/GRANT and no elevation attempt.
            await using (var authority = new NpgsqlCommand("SELECT rolsuper FROM pg_roles WHERE rolname=current_user", connection))
                if (await authority.ExecuteScalarAsync() is not true)
                    throw new InvalidOperationException("The isolated DDL-attempt sentinel requires an existing local test superuser; no privileges were changed.");
            try
            {
                await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{fixture._databaseName}\" TEMPLATE template0", connection))
                    await create.ExecuteNonQueryAsync();
                fixture._created = true;
                await using (var identify = new NpgsqlCommand("SELECT oid::bigint FROM pg_database WHERE datname=@name AND datdba=(SELECT oid FROM pg_roles WHERE rolname=current_user)", connection))
                {
                    identify.Parameters.AddWithValue("@name", fixture._databaseName);
                    fixture._databaseOid = Convert.ToInt64(await identify.ExecuteScalarAsync());
                }
                await fixture.InitializeAsync(nullableMask);
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        private IConfiguration Configuration(bool allowPartial)
        {
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _isolatedConnection,
                ["ConnectionStrings:SystemConnection"] = _isolatedConnection,
                ["Rls:EnforceTenantContext"] = "false",
                ["Samsara:MaxPagesPerSync"] = "1",
                ["Samsara:InterPageDelayMs"] = "0",
            };
            // Omission deliberately tests the real configuration default (off).
            if (allowPartial) settings["Samsara:AllowPartialGpsMeasurements"] = "true";
            return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        }

        private async Task InitializeAsync(int nullableMask)
        {
            await Db.ExecuteAsync("""
                CREATE TABLE integrations(company_id BIGINT NOT NULL,id BIGINT PRIMARY KEY,status TEXT NOT NULL,
                    config_json JSONB NOT NULL,operation_generation BIGINT NOT NULL,operation_lease_token UUID,
                    operation_lease_expires_at TIMESTAMPTZ,provider_last_event_at TIMESTAMPTZ,
                    last_sync_at TIMESTAMPTZ,sync_label TEXT,sync_last_completed_at TIMESTAMPTZ,
                    sync_last_ok BOOLEAN,updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW());
                CREATE TABLE eld_devices(id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,company_id BIGINT,
                    device_serial TEXT,provider TEXT,status TEXT,last_seen_at TIMESTAMPTZ);
                CREATE TABLE location_events(id BIGINT PRIMARY KEY,speed_mph NUMERIC NOT NULL,heading SMALLINT NOT NULL);
                CREATE TABLE latest_vehicle_positions(id BIGINT PRIMARY KEY,speed_mph NUMERIC NOT NULL,heading SMALLINT NOT NULL,source TEXT);
                CREATE TABLE telemetry_live_asset_states(id BIGINT PRIMARY KEY,speed_mph NUMERIC NOT NULL,heading SMALLINT NOT NULL);
                CREATE TABLE telemetry_alerts(id BIGINT PRIMARY KEY);
                CREATE SEQUENCE provider_write_attempts;
                CREATE SEQUENCE runtime_ddl_attempts;
                CREATE FUNCTION refuse_provider_write() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM nextval('public.provider_write_attempts');
                    RAISE EXCEPTION 'synthetic provider write boundary';
                END $$;
                CREATE FUNCTION refuse_runtime_ddl() RETURNS event_trigger LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM nextval('public.runtime_ddl_attempts');
                    RAISE EXCEPTION 'synthetic runtime DDL boundary';
                END $$;
                """);
            var columns = new[]
            {
                ("location_events", "speed_mph"), ("location_events", "heading"),
                ("latest_vehicle_positions", "speed_mph"), ("latest_vehicle_positions", "heading"),
                ("telemetry_live_asset_states", "speed_mph"), ("telemetry_live_asset_states", "heading"),
            };
            for (var bit = 0; bit < columns.Length; bit++)
                if ((nullableMask & (1 << bit)) != 0)
                    await Db.ExecuteAsync($"ALTER TABLE public.{columns[bit].Item1} ALTER COLUMN {columns[bit].Item2} DROP NOT NULL");
            await Db.ExecuteAsync(
                "INSERT INTO integrations(company_id,id,status,config_json,operation_generation,operation_lease_token,operation_lease_expires_at) VALUES(7,11,'Connected',@config::jsonb,0,@lease,NOW()+INTERVAL '3 minutes')",
                command =>
                {
                    command.Parameters.AddWithValue("@config", JsonSerializer.Serialize(new { syncCursor = BeforeCursor, marker = "preserve-schema-refusal" }));
                    command.Parameters.AddWithValue("@lease", Operation.LeaseToken);
                });
            foreach (var table in ProviderTables)
                await Db.ExecuteAsync($"CREATE TRIGGER refuse_provider_write BEFORE INSERT OR UPDATE OR DELETE ON public.{table} FOR EACH STATEMENT EXECUTE FUNCTION public.refuse_provider_write()");
            await Db.ExecuteAsync("CREATE EVENT TRIGGER refuse_runtime_ddl ON ddl_command_start EXECUTE FUNCTION public.refuse_runtime_ddl()");

            // Self-check both rollback-independent sentinels before measurement.
            var dml = await Assert.ThrowsAsync<PostgresException>(() => Db.ExecuteAsync("INSERT INTO eld_devices(company_id,device_serial) VALUES(7,'sentinel-self-check')"));
            Assert.Equal("synthetic provider write boundary", dml.MessageText);
            var ddl = await Assert.ThrowsAsync<PostgresException>(() => Db.ExecuteAsync("CREATE TABLE sentinel_should_not_exist(id BIGINT)"));
            Assert.Equal("synthetic runtime DDL boundary", ddl.MessageText);
            Assert.Equal(1, await AttemptCountAsync("provider_write_attempts"));
            Assert.Equal(1, await AttemptCountAsync("runtime_ddl_attempts"));
            await Db.ExecuteAsync("SELECT setval('public.provider_write_attempts',1,false),setval('public.runtime_ddl_attempts',1,false)");
        }

        private static readonly string[] ProviderTables = ["eld_devices", "location_events", "latest_vehicle_positions", "telemetry_live_asset_states", "telemetry_alerts"];

        private HttpMessageHandler Handler() => new JsonHandler(request =>
        {
            Requests.Add(request.RequestUri!);
            return JsonSerializer.Serialize(new
            {
                data = new[] { new { id = "synthetic-schema-vehicle", gps = new[] { new { time = DateTimeOffset.UtcNow.AddMinutes(-1), latitude = 34.05, longitude = -118.24 } } } },
                pagination = new { endCursor = ProviderCursor, hasNextPage = true },
            });
        });

        public async Task<SamsaraSync.SyncSummary> RunPageAsync(bool allowPartial)
        {
            using var handler = Handler();
            using var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://samsara.invalid") };
            using var services = new ServiceCollection().AddSingleton(Db).BuildServiceProvider();
            return await new SamsaraSync(client, services.GetRequiredService<IServiceScopeFactory>(), NullLogger.Instance, allowPartial)
                .RunAsync(Operation, BeforeCursor, CancellationToken.None);
        }

        public async Task<ConnectorResult> RunConnectorAsync(bool allowPartial)
        {
            using var handler = Handler();
            using var services = new ServiceCollection().AddSingleton(Db).BuildServiceProvider();
            var connector = new SamsaraConnector(new Factory(handler), services.GetRequiredService<IServiceScopeFactory>(),
                Configuration(allowPartial), NullLogger<SamsaraConnector>.Instance);
            using var body = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                companyId = Operation.CompanyId, integrationId = Operation.IntegrationId,
                operationGeneration = Operation.Generation, operationLeaseToken = Operation.LeaseToken,
                cursor = BeforeCursor,
            }));
            return await connector.RunActionAsync("sync", new Dictionary<string, string?> { ["apiToken"] = "synthetic-schema-token" },
                body.RootElement, CancellationToken.None);
        }

        public Task<long> NullableCountAsync() => Db.ScalarLongAsync("""
            SELECT COUNT(*) FROM information_schema.columns WHERE table_schema='public'
            AND table_name IN ('location_events','latest_vehicle_positions','telemetry_live_asset_states')
            AND column_name IN ('speed_mph','heading') AND is_nullable='YES'
            """);

        public async Task<string> SchemaFingerprintAsync() => (await Db.QuerySingleAsync("""
            SELECT md5(string_agg(table_name || ':' || column_name || ':' || data_type || ':' || is_nullable,
                '|' ORDER BY table_name,ordinal_position)) fingerprint
            FROM information_schema.columns WHERE table_schema='public'
            """))!["fingerprint"]!.ToString()!;

        public async Task<string> IntegrationVersionAsync() => (await Db.QuerySingleAsync(
            "SELECT xmin::text version FROM integrations WHERE company_id=7 AND id=11"))!["version"]!.ToString()!;

        public async Task<long> AttemptCountAsync(string sequence)
        {
            if (sequence is not ("provider_write_attempts" or "runtime_ddl_attempts"))
                throw new ArgumentOutOfRangeException(nameof(sequence));
            var row = (await Db.QuerySingleAsync($"SELECT last_value,is_called FROM public.{sequence}"))!;
            return row["isCalled"] is true ? Convert.ToInt64(row["lastValue"]) : 0;
        }

        public async Task AssertTablesEmptyAsync()
        {
            foreach (var table in ProviderTables)
                Assert.Equal(0, await Db.ScalarLongAsync($"SELECT COUNT(*) FROM public.{table}"));
        }

        public async Task AssertNoProviderWritesOrDdlAsync(string fingerprint)
        {
            Assert.Equal(0, await AttemptCountAsync("provider_write_attempts"));
            Assert.Equal(0, await AttemptCountAsync("runtime_ddl_attempts"));
            Assert.Equal(fingerprint, await SchemaFingerprintAsync());
            await AssertTablesEmptyAsync();
        }

        public async Task AssertFinalizedRefusalAsync()
        {
            var row = (await Db.QuerySingleAsync("SELECT status,config_json,sync_last_ok,sync_last_completed_at,provider_last_event_at,last_sync_at,operation_lease_token FROM integrations WHERE company_id=7 AND id=11"))!;
            Assert.Equal("Error", row["status"]);
            Assert.Equal(false, row["syncLastOk"]);
            Assert.NotNull(row["syncLastCompletedAt"]);
            Assert.Null(row["providerLastEventAt"]);
            Assert.Null(row["lastSyncAt"]);
            Assert.Null(row["operationLeaseToken"]);
            using var config = JsonDocument.Parse(row["configJson"]!.ToString()!);
            Assert.Equal(BeforeCursor, config.RootElement.GetProperty("syncCursor").GetString());
            Assert.Equal("preserve-schema-refusal", config.RootElement.GetProperty("marker").GetString());
        }

        public async ValueTask DisposeAsync()
        {
            if (!_created) return;
            // Exact generated name + observed OID + current owner, no forced drops,
            // no global pool clearing, no table mutation in the base/shared database.
            await using var connection = new NpgsqlConnection(_adminConnection);
            await connection.OpenAsync();
            await using (var identify = new NpgsqlCommand("SELECT oid::bigint FROM pg_database WHERE datname=@name AND datdba=(SELECT oid FROM pg_roles WHERE rolname=current_user)", connection))
            {
                identify.Parameters.AddWithValue("@name", _databaseName);
                var oid = await identify.ExecuteScalarAsync();
                if (oid is null || _databaseOid != 0 && Convert.ToInt64(oid) != _databaseOid)
                    throw new InvalidOperationException("Refusing cleanup: owned test database identity changed.");
            }
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{_databaseName}\"", connection);
            await drop.ExecuteNonQueryAsync();
            _created = false;
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class JsonHandler(Func<HttpRequestMessage, string> body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body(request), Encoding.UTF8, "application/json"),
            });
        }
    }
}
