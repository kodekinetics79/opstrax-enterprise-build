using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

// Live Stage58 acceptance. Every data assertion runs as the real restricted runtime
// identities; the owner is used only to seed and clean uniquely-marked test records.
[Collection("fleet-identity-schema")]
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
    public async Task PrincipalScopes_IsolateTwoUsersAndTwoTenants_AndRejectSpoofTamperReplayExpiry()
    {
        var owner = await PreparedOwnerDbAsync();
        var app = RuntimeDb(maxPool: 3);
        var suffix = Guid.NewGuid().ToString("N");
        var companyA = await owner.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES(@code,'Principal RLS A','Transportation')",
            c => c.Parameters.AddWithValue("@code", "PRLA-" + suffix));
        var companyB = await owner.InsertAsync(
            "INSERT INTO companies(company_code,name,industry) VALUES(@code,'Principal RLS B','Transportation')",
            c => c.Parameters.AddWithValue("@code", "PRLB-" + suffix));
        var userA = await SeedPrincipal(owner, companyA, "A", suffix);
        var userB = await SeedPrincipal(owner, companyA, "B", suffix);
        var userC = await SeedPrincipal(owner, companyB, "C", suffix);
        var tokenA = "principal-A-" + suffix;
        var tokenB = "principal-B-" + suffix;
        var tokenC = "principal-C-" + suffix;
        await SeedMobileTokens(owner, companyA, userA, tokenA, userB, tokenB, companyB, userC, tokenC);

        try
        {
            async Task<string[]> Visible(long company, long user) =>
                await app.RunInTenantScopeAsync(company, user, async () =>
                    (await app.QueryAsync(
                        "SELECT push_token FROM mobile_device_tokens WHERE push_token=ANY(@tokens) ORDER BY push_token",
                        c => c.Parameters.AddWithValue("@tokens", new[] { tokenA, tokenB, tokenC })))
                    .Select(row => row["pushToken"]!.ToString()!).ToArray());

            Assert.Equal([tokenA], await Visible(companyA, userA));
            Assert.Equal([tokenB], await Visible(companyA, userB));
            Assert.Equal([tokenC], await Visible(companyB, userC));
            Assert.Equal(0, await app.RunInTenantScopeAsync(companyA, userA, () =>
                app.ExecuteAsync("UPDATE mobile_device_tokens SET app_version='spoofed' WHERE user_id=@other",
                    c => c.Parameters.AddWithValue("@other", userB))));
            await Assert.ThrowsAsync<PostgresException>(() => app.RunInTenantScopeAsync(companyA, userA, () =>
                app.ExecuteAsync(
                    "INSERT INTO mobile_device_tokens(company_id,user_id,product,platform,push_token,token_fingerprint) VALUES(@company,@other,'fleet','ios',@token,@fingerprint)",
                    c =>
                    {
                        c.Parameters.AddWithValue("@company", companyA);
                        c.Parameters.AddWithValue("@other", userB);
                        c.Parameters.AddWithValue("@token", "forged-" + suffix);
                        c.Parameters.AddWithValue("@fingerprint", new string('d', 64));
                    })));
            Assert.Empty(await app.RunInTenantScopeAsync(companyA, async () =>
                (await app.QueryAsync("SELECT push_token FROM mobile_device_tokens WHERE company_id=@company",
                    c => c.Parameters.AddWithValue("@company", companyA))).ToArray()));
            Assert.Equal(3, await app.RunInSystemScopeAsync(() => app.ScalarLongAsync(
                "SELECT COUNT(*) FROM mobile_device_tokens WHERE push_token=ANY(@tokens)",
                c => c.Parameters.AddWithValue("@tokens", new[] { tokenA, tokenB, tokenC }))));

            await AssertPrincipalTicketBindings(app, owner, companyA, userA, userB, tokenA, tokenB, tokenC);
        }
        finally
        {
            await owner.ExecuteAsync("DELETE FROM mobile_device_tokens WHERE company_id=ANY(@companies)",
                c => c.Parameters.AddWithValue("@companies", new[] { companyA, companyB }));
            await owner.ExecuteAsync("DELETE FROM users WHERE company_id=ANY(@companies)",
                c => c.Parameters.AddWithValue("@companies", new[] { companyA, companyB }));
            await owner.ExecuteAsync("DELETE FROM companies WHERE id=ANY(@companies)",
                c => c.Parameters.AddWithValue("@companies", new[] { companyA, companyB }));
        }
    }

    [Fact]
    public async Task EveryTenantTable_HasExactSharedBoundedAndPrivatePolicies_AndNoPublicPolicy()
    {
        var owner = await PreparedOwnerDbAsync();
        var violations = await owner.QueryAsync("""
            SELECT contract FROM (VALUES
              ('generic',opstrax_security.generic_policy_contract_valid()),
              ('special',opstrax_security.special_policy_contract_valid()),
              ('migration-owned',opstrax_security.migration_owned_policy_contract_valid()),
              ('private',opstrax_security.private_policy_contract_valid())
            ) checks(contract,valid) WHERE NOT valid
            UNION ALL SELECT 'public:'||tablename
              FROM pg_policies WHERE schemaname='public' AND roles='{public}'::name[]
            ORDER BY 1
            """);
        Assert.True(violations.Count == 0,
            "Terminal tenant policy violations: " + string.Join(", ", violations.Select(v => v["contract"])));
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
                "UPDATE service_heartbeats SET last_heartbeat_at=@heartbeat WHERE service_name=@name",
                c =>
                {
                    c.Parameters.AddWithValue("@heartbeat", now.UtcDateTime - FleetProductionReadinessService.CriticalWorkerStartupGrace - TimeSpan.FromMinutes(1));
                    c.Parameters.AddWithValue("@name", FleetProductionReadinessService.CriticalWorkerNames[1]);
                });
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
    public async Task FleetReadiness_RejectsPermissivePrivatePolicyDrift()
    {
        var owner = await PreparedOwnerDbAsync();
        var readiness = new FleetProductionReadinessService(
            RuntimeDb(), NullLogger<FleetProductionReadinessService>.Instance);
        try
        {
            await owner.ExecuteAsync("""
                DROP POLICY principal_app_select ON mobile_device_tokens;
                CREATE POLICY principal_app_select ON mobile_device_tokens FOR SELECT TO opstrax_app
                  USING (true OR (company_id=(SELECT opstrax_security.current_tenant_id())
                    AND user_id=(SELECT opstrax_security.current_user_id())))
                """);
            Assert.Equal(0, await owner.ScalarLongAsync(
                "SELECT CASE WHEN opstrax_security.private_policy_contract_valid() THEN 1 ELSE 0 END"));
            var drifted = await readiness.CheckAsync();
            Assert.False(drifted.Ready);
            Assert.True(drifted.TenantCoverageViolations > 0);
        }
        finally
        {
            await owner.ExecuteAsync("""
                DROP POLICY IF EXISTS principal_app_select ON mobile_device_tokens;
                CREATE POLICY principal_app_select ON mobile_device_tokens FOR SELECT TO opstrax_app
                  USING (company_id=(SELECT opstrax_security.current_tenant_id())
                    AND user_id=(SELECT opstrax_security.current_user_id()))
                """);
        }
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

    private static Task<long> SeedPrincipal(Database owner, long companyId, string label, string suffix) =>
        owner.InsertAsync(
            "INSERT INTO users(company_id,full_name,email,role_name,status,permissions_json) VALUES(@company,@name,@email,'Viewer','Active','[]'::jsonb)",
            c =>
            {
                c.Parameters.AddWithValue("@company", companyId);
                c.Parameters.AddWithValue("@name", "Principal " + label);
                c.Parameters.AddWithValue("@email", $"principal-{label}-{suffix}@example.invalid");
            });

    private static Task SeedMobileTokens(
        Database owner, long companyA, long userA, string tokenA, long userB, string tokenB,
        long companyB, long userC, string tokenC) => owner.ExecuteAsync(
        """
        INSERT INTO mobile_device_tokens(company_id,user_id,product,platform,push_token,token_fingerprint)
        VALUES (@companyA,@userA,'fleet','ios',@tokenA,@fingerprintA),
               (@companyA,@userB,'fleet','ios',@tokenB,@fingerprintB),
               (@companyB,@userC,'fleet','ios',@tokenC,@fingerprintC)
        """,
        c =>
        {
            c.Parameters.AddWithValue("@companyA", companyA); c.Parameters.AddWithValue("@userA", userA);
            c.Parameters.AddWithValue("@tokenA", tokenA); c.Parameters.AddWithValue("@fingerprintA", new string('a', 64));
            c.Parameters.AddWithValue("@userB", userB); c.Parameters.AddWithValue("@tokenB", tokenB);
            c.Parameters.AddWithValue("@fingerprintB", new string('b', 64));
            c.Parameters.AddWithValue("@companyB", companyB); c.Parameters.AddWithValue("@userC", userC);
            c.Parameters.AddWithValue("@tokenC", tokenC); c.Parameters.AddWithValue("@fingerprintC", new string('c', 64));
        });

    private static async Task AssertPrincipalTicketBindings(
        Database runtime, Database owner, long company, long user, long otherUser, params string[] tokens)
    {
        await using var appOne = new NpgsqlConnection(TestDb.AppConnectionString);
        await using var appTwo = new NpgsqlConnection(TestDb.AppConnectionString);
        await using var system = new NpgsqlConnection(TestDb.SystemConnectionString);
        await appOne.OpenAsync(); await appTwo.OpenAsync(); await system.OpenAsync();

        await using var tx = await appOne.BeginTransactionAsync();
        var binding = await Binding(appOne, tx);
        var ticket = await IssuePrincipal(system, company, user, binding.pid, binding.txid, 120);
        await SetLocal(appOne, tx, "app.tenant_ticket", ticket);
        Assert.Equal(user, await CurrentUser(appOne, tx));
        Assert.Equal(1, await CountPrivateTokens(appOne, tx, tokens));

        var tampered = ticket.Split(':');
        tampered[2] = otherUser.ToString();
        await SetLocal(appOne, tx, "app.tenant_ticket", string.Join(':', tampered));
        Assert.Null(await CurrentUser(appOne, tx));
        Assert.Equal(0, await CountPrivateTokens(appOne, tx, tokens));
        await SetLocal(appOne, tx, "app.tenant_ticket", ticket);

        await using (var wrongPid = await appTwo.BeginTransactionAsync())
        {
            await SetLocal(appTwo, wrongPid, "app.tenant_ticket", ticket);
            Assert.Null(await CurrentUser(appTwo, wrongPid));
            Assert.Equal(0, await CountPrivateTokens(appTwo, wrongPid, tokens));
            await wrongPid.RollbackAsync();
        }

        await tx.CommitAsync();
        await using (var replay = await appOne.BeginTransactionAsync())
        {
            await SetLocal(appOne, replay, "app.tenant_ticket", ticket);
            Assert.Null(await CurrentUser(appOne, replay));
            Assert.Equal(0, await CountPrivateTokens(appOne, replay, tokens));
            await replay.RollbackAsync();
        }

        await using (var expiry = await appOne.BeginTransactionAsync())
        {
            var expiryBinding = await Binding(appOne, expiry);
            var expiring = await IssuePrincipal(system, company, user, expiryBinding.pid, expiryBinding.txid, 5);
            await SetLocal(appOne, expiry, "app.tenant_ticket", expiring);
            Assert.Equal(user, await CurrentUser(appOne, expiry));
            await Task.Delay(TimeSpan.FromSeconds(6.2));
            Assert.Null(await CurrentUser(appOne, expiry));
            Assert.Equal(0, await CountPrivateTokens(appOne, expiry, tokens));
            await expiry.RollbackAsync();
        }

        await owner.ExecuteAsync("UPDATE users SET status='Suspended' WHERE id=@user",
            c => c.Parameters.AddWithValue("@user", user));
        await Assert.ThrowsAsync<PostgresException>(() => runtime.BeginTenantScopeAsync(company, user));
        await owner.ExecuteAsync("UPDATE users SET status='Active' WHERE id=@user",
            c => c.Parameters.AddWithValue("@user", user));
    }

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
            "2026_08_11_stage76_telematics_security_hardening.sql",
            "2026_09_07_stage112_camera_provider_ingest_spine.sql",
            "2026_09_07_stage115_device_compatibility_candidate_registry.sql",
            "2026_09_07_stage116_device_connectivity_profiles.sql",
            "2026_09_07_stage117_device_firmware_campaign_planning.sql",
            "2026_09_07_stage118_device_rma_replacement.sql",
            "2026_09_07_stage119_device_remote_command_governance.sql",
            "2026_09_07_stage120_device_connectivity_observations.sql",
            "2026_09_07_stage121_device_installation_work_packages.sql",
            "2026_09_07_stage122_installation_work_package_links.sql",
            "2026_09_07_stage123_device_retirement.sql",
            "2026_09_07_stage124_rma_support_ownership.sql",
            "2026_09_07_stage125_device_spare_pool.sql",
            "2026_09_07_stage126_device_support_tier_history.sql",
            "2026_09_08_stage128_device_compatibility_capability_catalog.sql",
            "2026_09_08_stage129_latest_device_signal_projection.sql",
            "2026_09_08_stage130_canonical_diagnostic_evidence_identity.sql",
            "2026_09_08_stage132_private_user_row_authority.sql",
        })
        {
            await ExecuteMigrationWithDeadlockRetryAsync(
                owner,
                File.ReadAllText(Path.Combine(root, "database", "migrations", migration)));
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

    private static async Task ExecuteMigrationWithDeadlockRetryAsync(Database owner, string sql)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await owner.ExecuteAsync(sql);
                return;
            }
            catch (PostgresException ex) when (
                ex.SqlState == PostgresErrorCodes.DeadlockDetected && attempt < maxAttempts)
            {
                // The integration host can finish a startup schema reconciliation while
                // this test fixture replays the terminal contract. PostgreSQL correctly
                // aborts one participant; retry only that idempotent fixture migration.
                await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt));
            }
        }
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

    private static async Task<string> IssuePrincipal(
        NpgsqlConnection system, long tenant, long user, int pid, long txid, int ttl)
    {
        await using var command = new NpgsqlCommand(
            "SELECT opstrax_security.issue_principal_ticket(@tenant,@user,@pid,@txid,@ttl)", system);
        command.Parameters.AddWithValue("@tenant", tenant);
        command.Parameters.AddWithValue("@user", user);
        command.Parameters.AddWithValue("@pid", pid);
        command.Parameters.AddWithValue("@txid", txid);
        command.Parameters.AddWithValue("@ttl", ttl);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<long?> CurrentUser(NpgsqlConnection connection, NpgsqlTransaction tx)
    {
        await using var command = new NpgsqlCommand("SELECT opstrax_security.current_user_id()", connection, tx);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : Convert.ToInt64(value);
    }

    private static async Task<long> CountPrivateTokens(
        NpgsqlConnection connection, NpgsqlTransaction tx, params string[] tokens)
    {
        await using var command = new NpgsqlCommand(
            "SELECT COUNT(*) FROM mobile_device_tokens WHERE push_token=ANY(@tokens)", connection, tx);
        command.Parameters.AddWithValue("@tokens", tokens);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
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
