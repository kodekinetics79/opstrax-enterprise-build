using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// Live Stage58 acceptance. Every data assertion runs as the real restricted runtime
// identities; the owner is used only to seed and clean uniquely-marked test records.
public sealed class RlsTenantIsolationPostgresTests
{
    private static Database OwnerDb() => CreateDb(TestDb.ConnectionString, false);

    private static Database RuntimeDb(TenantScopeAccessor? accessor = null, int maxPool = 5)
    {
        var app = new NpgsqlConnectionStringBuilder(TestDb.AppConnectionString)
        {
            Pooling = true,
            MinPoolSize = 0,
            MaxPoolSize = maxPool,
        };
        var system = new NpgsqlConnectionStringBuilder(TestDb.SystemConnectionString)
        {
            Pooling = true,
            MinPoolSize = 0,
            MaxPoolSize = maxPool,
        };
        return CreateDb(app.ConnectionString, true, system.ConnectionString, accessor);
    }

    private static Database CreateDb(
        string appConnection,
        bool enforceRls,
        string? systemConnection = null,
        TenantScopeAccessor? accessor = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = appConnection,
                ["ConnectionStrings:SystemConnection"] = systemConnection,
                ["Rls:EnforceTenantContext"] = enforceRls.ToString(),
                ["Rls:TenantTicketTtlSeconds"] = "120",
            })
            .Build();
        return new Database(config, accessor ?? new TenantScopeAccessor());
    }

    [Fact]
    public async Task SignedScopes_IsolateTwoTenants_UnderConcurrentPoolReuse()
    {
        var owner = await PreparedOwnerDbAsync();
        var app = RuntimeDb(maxPool: 3);
        var suffix = Guid.NewGuid().ToString("N");
        var tenantA = 930_000_000L + Random.Shared.Next(1, 400_000);
        var tenantB = 940_000_000L + Random.Shared.Next(1, 400_000);
        var markerA = $"TKT-A-{suffix}";
        var markerB = $"TKT-B-{suffix}";
        await SeedMarkers(owner, tenantA, markerA, tenantB, markerB);

        try
        {
            async Task AssertTenant(long tenant, string own, string foreign)
            {
                var visible = await app.RunInTenantScopeAsync(tenant, () => VisibleMarkers(app, markerA, markerB));
                Assert.Equal([own], visible);
                Assert.DoesNotContain(foreign, visible);
            }

            var interleaved = Enumerable.Range(0, 30)
                .SelectMany(_ => new[]
                {
                    Task.Run(() => AssertTenant(tenantA, markerA, markerB)),
                    Task.Run(() => AssertTenant(tenantB, markerB, markerA)),
                });
            await Task.WhenAll(interleaved);

            Assert.Empty(await VisibleMarkers(app, markerA, markerB));
            var systemVisible = await app.RunInSystemScopeAsync(() => VisibleMarkers(app, markerA, markerB));
            Assert.Equal([markerA, markerB], systemVisible);
            Assert.Equal("opstrax_system",
                (await app.QuerySingleInSystemScopeAsync("SELECT current_user AS username"))!["username"]);

            NpgsqlConnection.ClearAllPools();
            Assert.Equal([markerA],
                await app.RunInTenantScopeAsync(tenantA, () => VisibleMarkers(app, markerA, markerB)));
            Assert.Empty(await VisibleMarkers(app, markerA, markerB));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await DeleteMarkers(owner, markerA, markerB);
        }
    }

    [Fact]
    public async Task SystemTransaction_RestoresTenantScope_AndRollsBackAtomically()
    {
        var owner = await PreparedOwnerDbAsync();
        var accessor = new TenantScopeAccessor();
        var app = RuntimeDb(accessor, maxPool: 3);
        var suffix = Guid.NewGuid().ToString("N");
        var tenantA = 950_000_000L + Random.Shared.Next(1, 400_000);
        var tenantB = 960_000_000L + Random.Shared.Next(1, 400_000);
        var markerA = $"TKT-NEST-A-{suffix}";
        var markerB = $"TKT-NEST-B-{suffix}";
        var committed = $"TKT-SYS-C-{suffix}";
        var rolledBack = $"TKT-SYS-R-{suffix}";
        await SeedMarkers(owner, tenantA, markerA, tenantB, markerB);

        try
        {
            await using var tenantScope = await app.BeginTenantScopeAsync(tenantA);
            accessor.Current = tenantScope;
            var original = accessor.Current;
            try
            {
                Assert.Equal([markerA], await VisibleMarkers(app, markerA, markerB, committed, rolledBack));
                await app.RunInSystemTransactionAsync(async () =>
                {
                    Assert.NotSame(original, accessor.Current);
                    await InsertMarker(app, tenantB, committed);
                    return true;
                });
                Assert.Same(original, accessor.Current);
                Assert.Equal([markerA], await VisibleMarkers(app, markerA, markerB, committed, rolledBack));

                await Assert.ThrowsAsync<InvalidOperationException>(() => app.RunInSystemTransactionAsync<bool>(async () =>
                {
                    await InsertMarker(app, tenantB, rolledBack);
                    throw new InvalidOperationException("forced rollback");
                }));
                Assert.Same(original, accessor.Current);
                Assert.Equal([markerA], await VisibleMarkers(app, markerA, markerB, committed, rolledBack));
                await tenantScope.CompleteAsync();
            }
            finally { accessor.Current = null; }

            var tenantBVisible = await app.RunInTenantScopeAsync(tenantB,
                () => VisibleMarkers(app, markerA, markerB, committed, rolledBack));
            Assert.Equal([markerB, committed], tenantBVisible);
            Assert.Equal(0, await owner.ScalarLongAsync(
                "SELECT COUNT(*) FROM dvir_reports WHERE report_number=@m",
                c => c.Parameters.AddWithValue("@m", rolledBack)));
        }
        finally
        {
            await DeleteMarkers(owner, markerA, markerB, committed, rolledBack);
        }
    }

    [Fact]
    public async Task ForgedLegacyTamperedReplayedAndExpiredTickets_AllFailClosed()
    {
        var owner = await PreparedOwnerDbAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var tenantA = 970_000_000L + Random.Shared.Next(1, 400_000);
        var tenantB = 980_000_000L + Random.Shared.Next(1, 400_000);
        var markerA = $"TKT-BOUND-A-{suffix}";
        var markerB = $"TKT-BOUND-B-{suffix}";
        await SeedMarkers(owner, tenantA, markerA, tenantB, markerB);

        await using var appOne = new NpgsqlConnection(TestDb.AppConnectionString);
        await using var appTwo = new NpgsqlConnection(TestDb.AppConnectionString);
        await using var system = new NpgsqlConnection(TestDb.SystemConnectionString);
        await appOne.OpenAsync();
        await appTwo.OpenAsync();
        await system.OpenAsync();

        try
        {
            await Assert.ThrowsAsync<PostgresException>(async () =>
            {
                await using var denied = new NpgsqlCommand(
                    "SELECT opstrax_security.issue_tenant_ticket(1,pg_backend_pid(),txid_current()::bigint,120)", appOne);
                await denied.ExecuteScalarAsync();
            });

            await using (var legacyTx = await appOne.BeginTransactionAsync())
            {
                await SetLocal(appOne, legacyTx, "app.current_tenant_id", tenantA.ToString());
                await SetLocal(appOne, legacyTx, "app.platform_admin", "on");
                Assert.Equal(0, await CountMarkers(appOne, legacyTx, markerA, markerB));
                await legacyTx.RollbackAsync();
            }

            await using var txOne = await appOne.BeginTransactionAsync();
            var (pid, txid) = await Binding(appOne, txOne);
            var ticket = await Issue(system, tenantA, pid, txid, 120);
            await SetLocal(appOne, txOne, "app.tenant_ticket", ticket);
            Assert.Equal(1, await CountMarkers(appOne, txOne, markerA, markerB));

            var parts = ticket.Split(':');
            parts[1] = tenantB.ToString();
            await SetLocal(appOne, txOne, "app.tenant_ticket", string.Join(':', parts));
            Assert.Equal(0, await CountMarkers(appOne, txOne, markerA, markerB));
            await SetLocal(appOne, txOne, "app.tenant_ticket", ticket);

            await using (var wrongPidTx = await appTwo.BeginTransactionAsync())
            {
                await SetLocal(appTwo, wrongPidTx, "app.tenant_ticket", ticket);
                Assert.Equal(0, await CountMarkers(appTwo, wrongPidTx, markerA, markerB));
                await wrongPidTx.RollbackAsync();
            }

            await txOne.CommitAsync();
            await using (var replayTx = await appOne.BeginTransactionAsync())
            {
                await SetLocal(appOne, replayTx, "app.tenant_ticket", ticket);
                Assert.Equal(0, await CountMarkers(appOne, replayTx, markerA, markerB));
                await replayTx.RollbackAsync();
            }

            await using (var expiryTx = await appOne.BeginTransactionAsync())
            {
                var expiryBinding = await Binding(appOne, expiryTx);
                var expiring = await Issue(system, tenantA, expiryBinding.pid, expiryBinding.txid, 5);
                await SetLocal(appOne, expiryTx, "app.tenant_ticket", expiring);
                Assert.Equal(1, await CountMarkers(appOne, expiryTx, markerA, markerB));
                await Task.Delay(TimeSpan.FromSeconds(6.2));
                Assert.Equal(0, await CountMarkers(appOne, expiryTx, markerA, markerB));
                await expiryTx.RollbackAsync();
            }
        }
        finally
        {
            await DeleteMarkers(owner, markerA, markerB);
        }
    }

    [Fact]
    public async Task EveryTenantTable_HasExactStage58Policies_AndNoPublicPolicy()
    {
        var owner = await PreparedOwnerDbAsync();
        var violations = await owner.QueryAsync("""
            WITH tenant_tables AS (
              SELECT c.oid,c.relname,
                CASE WHEN c.relname='companies' THEN 'id'
                     WHEN EXISTS(SELECT 1 FROM information_schema.columns x WHERE x.table_schema='public' AND x.table_name=c.relname AND x.column_name='company_id' AND x.data_type='bigint') THEN 'company_id'
                     ELSE 'tenant_id' END tenant_col
              FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
              WHERE n.nspname='public' AND c.relkind IN ('r','p')
                AND c.relname NOT IN ('platform_invoices','gps_gateway_replay','platform_impersonation_sessions','roles','report_catalog')
                AND (c.relname='companies' OR EXISTS(SELECT 1 FROM information_schema.columns x
                  WHERE x.table_schema='public' AND x.table_name=c.relname
                    AND x.column_name IN ('company_id','tenant_id') AND x.data_type='bigint'))
            )
            SELECT t.relname FROM tenant_tables t JOIN pg_class c ON c.oid=t.oid
            WHERE NOT c.relrowsecurity OR NOT c.relforcerowsecurity
               OR (SELECT COUNT(*) FROM pg_policies p WHERE p.schemaname='public' AND p.tablename=t.relname)<>2
               OR NOT EXISTS(SELECT 1 FROM pg_policies p WHERE p.schemaname='public' AND p.tablename=t.relname
                  AND p.policyname='tenant_ticket_app' AND p.roles='{opstrax_app}'::name[] AND p.cmd='ALL'
                  AND p.qual LIKE '%'||t.tenant_col||'%opstrax_security.current_tenant_id()%'
                  AND p.qual LIKE '%SELECT%' AND p.with_check=p.qual)
               OR NOT EXISTS(SELECT 1 FROM pg_policies p WHERE p.schemaname='public' AND p.tablename=t.relname
                  AND p.policyname='system_control_plane' AND p.roles='{opstrax_system}'::name[] AND p.cmd='ALL'
                  AND p.qual='true' AND p.with_check='true')
            UNION ALL
            SELECT tablename FROM pg_policies WHERE schemaname='public' AND roles='{public}'::name[]
            ORDER BY 1
            """);
        Assert.True(violations.Count == 0,
            "Stage58 tenant policy violations: " + string.Join(", ", violations.Select(v => v["relname"] ?? v["tablename"])));
    }

    [Fact]
    public async Task FleetReadiness_AcceptsTerminalStage58And59Contract()
    {
        await PreparedOwnerDbAsync();
        var readiness = new FleetProductionReadinessService(
            RuntimeDb(), NullLogger<FleetProductionReadinessService>.Instance);
        var result = await readiness.CheckAsync();
        Assert.True(result.Ready, $"Terminal Fleet DB readiness failed: {result}");
        Assert.True(result.TenantTicketMigrationApplied);
        Assert.True(result.DataProtectionKeyRingMigrationApplied);
    }

    [Fact]
    public async Task FleetReadiness_EnforcesExpectedWorkerRoster_AfterStartupGrace_AndRecovers()
    {
        var owner = await PreparedOwnerDbAsync();
        var now = DateTimeOffset.UtcNow;
        var afterGrace = new FleetProductionReadinessService(
            RuntimeDb(), NullLogger<FleetProductionReadinessService>.Instance,
            new FixedTimeProvider(now), now - FleetProductionReadinessService.CriticalWorkerStartupGrace - TimeSpan.FromSeconds(1));

        try
        {
            await owner.ExecuteAsync(
                "DELETE FROM service_heartbeats WHERE service_name=ANY(@names)",
                c => c.Parameters.AddWithValue("@names", FleetProductionReadinessService.CriticalWorkerNames));

            var empty = await afterGrace.CheckAsync();
            Assert.False(empty.Ready);
            Assert.Equal(FleetProductionReadinessService.CriticalWorkerNames.Length, empty.CriticalWorkerViolations);
            Assert.Equal(FleetProductionReadinessService.CriticalWorkerNames.Length, empty.MissingCriticalWorkers);

            await SeedFreshCriticalWorkersAsync(owner);
            await owner.ExecuteAsync(
                "DELETE FROM service_heartbeats WHERE service_name=@name",
                c => c.Parameters.AddWithValue("@name", FleetProductionReadinessService.CriticalWorkerNames[0]));
            var missing = await afterGrace.CheckAsync();
            Assert.False(missing.Ready);
            Assert.Equal(1, missing.CriticalWorkerViolations);
            Assert.Equal(1, missing.MissingCriticalWorkers);

            await SeedFreshCriticalWorkersAsync(owner);
            await owner.ExecuteAsync(
                "UPDATE service_heartbeats SET last_heartbeat_at=NOW()-INTERVAL '11 minutes' WHERE service_name=@name",
                c => c.Parameters.AddWithValue("@name", FleetProductionReadinessService.CriticalWorkerNames[1]));
            var stale = await afterGrace.CheckAsync();
            Assert.False(stale.Ready);
            Assert.Equal(1, stale.CriticalWorkerViolations);
            Assert.Equal(1, stale.StaleCriticalWorkers);

            await SeedFreshCriticalWorkersAsync(owner);
            await owner.ExecuteAsync(
                "UPDATE service_heartbeats SET last_heartbeat_at=NOW()-INTERVAL '3 minutes' WHERE service_name=@name",
                c => c.Parameters.AddWithValue("@name", FleetProductionReadinessService.CriticalWorkerNames[1]));
            var priorProcessHeartbeat = await afterGrace.CheckAsync();
            Assert.False(priorProcessHeartbeat.Ready);
            Assert.Equal(1, priorProcessHeartbeat.CriticalWorkerViolations);
            Assert.Equal(1, priorProcessHeartbeat.StaleCriticalWorkers);

            await SeedFreshCriticalWorkersAsync(owner);
            await owner.ExecuteAsync(
                "UPDATE service_heartbeats SET consecutive_failures=3,last_run_status='failed' WHERE service_name=@name",
                c => c.Parameters.AddWithValue("@name", FleetProductionReadinessService.CriticalWorkerNames[2]));
            var failed = await afterGrace.CheckAsync();
            Assert.False(failed.Ready);
            Assert.Equal(1, failed.CriticalWorkerViolations);
            Assert.Equal(1, failed.FailedCriticalWorkers);

            await SeedFreshCriticalWorkersAsync(owner);
            var recovered = await afterGrace.CheckAsync();
            Assert.True(recovered.Ready, $"Fresh critical-worker roster did not recover readiness: {recovered}");
            Assert.Equal(0, recovered.CriticalWorkerViolations);

            await owner.ExecuteAsync(
                "DELETE FROM service_heartbeats WHERE service_name=ANY(@names)",
                c => c.Parameters.AddWithValue("@names", FleetProductionReadinessService.CriticalWorkerNames));
            var duringGrace = new FleetProductionReadinessService(
                RuntimeDb(), NullLogger<FleetProductionReadinessService>.Instance,
                new FixedTimeProvider(now), now - TimeSpan.FromSeconds(30));
            var starting = await duringGrace.CheckAsync();
            Assert.True(starting.Ready, $"Startup grace should permit workers to publish first heartbeats: {starting}");
            Assert.True(starting.CriticalWorkerStartupGraceActive);
            Assert.Equal(0, starting.CriticalWorkerViolations);
            Assert.Equal(FleetProductionReadinessService.CriticalWorkerNames.Length, starting.RawCriticalWorkerViolations);
        }
        finally
        {
            await SeedFreshCriticalWorkersAsync(owner);
        }
    }

    [Fact]
    public async Task FleetReadiness_RejectsPermissiveSpecialPolicyDrift()
    {
        var owner = await PreparedOwnerDbAsync();
        var readiness = new FleetProductionReadinessService(
            RuntimeDb(), NullLogger<FleetProductionReadinessService>.Instance);
        try
        {
            await owner.ExecuteAsync("""
                DROP POLICY roles_app_select ON roles;
                CREATE POLICY roles_app_select ON roles FOR SELECT TO opstrax_app
                  USING (true OR company_id=(SELECT opstrax_security.current_tenant_id()))
                """);
            var drifted = await readiness.CheckAsync();
            Assert.False(drifted.Ready);
            Assert.True(drifted.TenantCoverageViolations > 0);
        }
        finally
        {
            await owner.ExecuteAsync("""
                DROP POLICY IF EXISTS roles_app_select ON roles;
                CREATE POLICY roles_app_select ON roles FOR SELECT TO opstrax_app
                  USING (company_id IS NULL OR company_id=(SELECT opstrax_security.current_tenant_id()))
                """);
        }
        Assert.True((await readiness.CheckAsync()).Ready);
    }

    [Fact]
    public async Task FleetReadiness_RejectsPermissiveGenericPolicyDrift_AndRlsFailsClosed()
    {
        var owner = await PreparedOwnerDbAsync();
        var runtime = RuntimeDb();
        var readiness = new FleetProductionReadinessService(
            runtime, NullLogger<FleetProductionReadinessService>.Instance);
        var marker = $"ST58-GENERIC-DRIFT-{Guid.NewGuid():N}";

        try
        {
            await owner.ExecuteAsync(
                @"INSERT INTO dvir_reports(company_id,report_number,driver_id,vehicle_id,inspection_type,inspection_status)
                  VALUES (999999999,@marker,0,0,'Pre-Trip','Submitted')",
                c => c.Parameters.AddWithValue("@marker", marker));
            Assert.Empty(await VisibleMarkers(runtime, marker));

            await owner.ExecuteAsync("""
                DROP POLICY tenant_ticket_app ON dvir_reports;
                CREATE POLICY tenant_ticket_app ON dvir_reports AS PERMISSIVE FOR ALL TO opstrax_app
                  USING (true OR company_id=(SELECT opstrax_security.current_tenant_id()))
                  WITH CHECK (company_id=(SELECT opstrax_security.current_tenant_id()))
                """);

            Assert.Equal(new[] { marker }, await VisibleMarkers(runtime, marker));
            var drifted = await readiness.CheckAsync();
            Assert.False(drifted.Ready);
            Assert.True(drifted.TenantCoverageViolations > 0);
        }
        finally
        {
            await owner.ExecuteAsync("""
                DROP POLICY IF EXISTS tenant_ticket_app ON dvir_reports;
                CREATE POLICY tenant_ticket_app ON dvir_reports AS PERMISSIVE FOR ALL TO opstrax_app
                  USING (company_id=(SELECT opstrax_security.current_tenant_id()))
                  WITH CHECK (company_id=(SELECT opstrax_security.current_tenant_id()))
                """);
            await owner.ExecuteAsync("DELETE FROM dvir_reports WHERE report_number=@marker",
                c => c.Parameters.AddWithValue("@marker", marker));
        }

        Assert.Empty(await VisibleMarkers(runtime, marker));
        Assert.True((await readiness.CheckAsync()).Ready);
    }

    [Fact]
    public async Task FleetReadiness_RejectsMissingSpecialTablePrivilege()
    {
        var owner = await PreparedOwnerDbAsync();
        var readiness = new FleetProductionReadinessService(
            RuntimeDb(), NullLogger<FleetProductionReadinessService>.Instance);
        try
        {
            await owner.ExecuteAsync("REVOKE DELETE ON roles FROM opstrax_app");
            var privilege = await owner.QueryAsync(
                "SELECT has_table_privilege('opstrax_app','roles','DELETE') AS allowed");
            Assert.False(Convert.ToBoolean(privilege.Single()["allowed"]));
            var drifted = await readiness.CheckAsync();
            Assert.False(drifted.Ready);
            Assert.True(drifted.TenantGrantViolations > 0);
        }
        finally
        {
            await owner.ExecuteAsync("GRANT DELETE ON roles TO opstrax_app");
        }
        Assert.True((await readiness.CheckAsync()).Ready);
    }

    private static async Task SeedMarkers(Database owner, long tenantA, string markerA, long tenantB, string markerB) =>
        await owner.ExecuteAsync(
            @"INSERT INTO dvir_reports(company_id,report_number,driver_id,vehicle_id,inspection_type,inspection_status)
              VALUES (@a,@ma,0,0,'Pre-Trip','Submitted'),(@b,@mb,0,0,'Pre-Trip','Submitted')",
            c =>
            {
                c.Parameters.AddWithValue("@a", tenantA); c.Parameters.AddWithValue("@ma", markerA);
                c.Parameters.AddWithValue("@b", tenantB); c.Parameters.AddWithValue("@mb", markerB);
            });

    private static async Task<Database> PreparedOwnerDbAsync()
    {
        var owner = OwnerDb();
        var root = FindRoot();
        // The complete suite intentionally exercises owner-boot schema services that
        // can recreate pre-terminal objects. Reconcile the whole Fleet release
        // contract so these live security tests are order-independent, matching the
        // predeploy runner's terminal ordering.
        foreach (var migration in new[]
        {
            "2026_07_30_stage50_fleet_production_contract.sql",
            "2026_07_30_stage51_production_runtime_support.sql",
            "2026_07_30_stage52_fleet_identity_uniqueness.sql",
            "2026_07_30_stage54_cold_chain_device_integrity.sql",
            "2026_07_30_stage55_fleet_runtime_route_contract.sql",
            "2026_07_30_stage56_asset_type_integrity.sql",
            "2026_07_30_stage57_workforce_schedule_tenant_integrity.sql",
            "2026_07_31_stage58_nonforgeable_tenant_ticket.sql",
            "2026_07_31_stage59_data_protection_key_ring.sql",
            "2026_08_02_stage67_telematics_diagnostics_integrity.sql",
        })
        {
            await owner.ExecuteAsync(File.ReadAllText(Path.Combine(root, "database", "migrations", migration)));
            await owner.ExecuteAsync(
                "INSERT INTO schema_migrations(version,description) VALUES(@version,'test terminal reconciliation') ON CONFLICT(version) DO NOTHING",
                c => c.Parameters.AddWithValue("@version", Path.GetFileNameWithoutExtension(migration)));
        }
        // Stage58 is the terminal, non-forgeable replacement for the legacy Stage53
        // GUC policies. Do not replay Stage53 after Stage58-era tables exist; retain
        // its historical ledger marker, as the production predeploy runner does.
        await owner.ExecuteAsync(
            "INSERT INTO schema_migrations(version,description) VALUES('2026_07_30_stage53_tenant_rls_reconciliation','superseded by Stage58 in test terminal reconciliation') ON CONFLICT(version) DO NOTHING");
        await SeedFreshCriticalWorkersAsync(owner);
        return owner;
    }

    private static Task SeedFreshCriticalWorkersAsync(Database owner) => owner.ExecuteAsync(
        @"INSERT INTO service_heartbeats
            (service_name,last_heartbeat_at,last_run_at,last_run_status,consecutive_failures,last_error_safe,updated_at)
          SELECT name,NOW(),NOW(),'succeeded',0,NULL,NOW() FROM unnest(@names::text[]) AS name
          ON CONFLICT(service_name) DO UPDATE SET
            last_heartbeat_at=NOW(),last_run_at=NOW(),last_run_status='succeeded',
            consecutive_failures=0,last_error_safe=NULL,updated_at=NOW()",
        c => c.Parameters.AddWithValue("@names", FleetProductionReadinessService.CriticalWorkerNames));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static async Task InsertMarker(Database db, long tenant, string marker) =>
        await db.ExecuteAsync(
            @"INSERT INTO dvir_reports(company_id,report_number,driver_id,vehicle_id,inspection_type,inspection_status)
              VALUES (@tenant,@marker,0,0,'Pre-Trip','Submitted')",
            c => { c.Parameters.AddWithValue("@tenant", tenant); c.Parameters.AddWithValue("@marker", marker); });

    private static async Task DeleteMarkers(Database owner, params string[] markers) =>
        await owner.ExecuteAsync("DELETE FROM dvir_reports WHERE report_number=ANY(@markers)",
            c => c.Parameters.AddWithValue("@markers", markers));

    private static async Task<string[]> VisibleMarkers(Database db, params string[] markers)
    {
        var rows = await db.QueryAsync(
            "SELECT report_number FROM dvir_reports WHERE report_number=ANY(@markers) ORDER BY report_number",
            c => c.Parameters.AddWithValue("@markers", markers));
        return rows.Select(row => row["reportNumber"]!.ToString()!).ToArray();
    }

    private static async Task<(int pid, long txid)> Binding(NpgsqlConnection connection, NpgsqlTransaction tx)
    {
        await using var command = new NpgsqlCommand("SELECT pg_backend_pid(),txid_current()::bigint", connection, tx);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt32(0), reader.GetInt64(1));
    }

    private static async Task<string> Issue(NpgsqlConnection system, long tenant, int pid, long txid, int ttl)
    {
        await using var command = new NpgsqlCommand(
            "SELECT opstrax_security.issue_tenant_ticket(@tenant,@pid,@txid,@ttl)", system);
        command.Parameters.AddWithValue("@tenant", tenant);
        command.Parameters.AddWithValue("@pid", pid);
        command.Parameters.AddWithValue("@txid", txid);
        command.Parameters.AddWithValue("@ttl", ttl);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task SetLocal(
        NpgsqlConnection connection, NpgsqlTransaction tx, string name, string value)
    {
        await using var command = new NpgsqlCommand("SELECT set_config(@name,@value,true)", connection, tx);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@value", value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountMarkers(
        NpgsqlConnection connection, NpgsqlTransaction tx, params string[] markers)
    {
        await using var command = new NpgsqlCommand(
            "SELECT COUNT(*) FROM dvir_reports WHERE report_number=ANY(@markers)", connection, tx);
        command.Parameters.AddWithValue("@markers", markers);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
