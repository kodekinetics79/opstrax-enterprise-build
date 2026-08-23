using Npgsql;
using Microsoft.Extensions.Configuration;
using Opstrax.Api.Data;

namespace Opstrax.Tests;

public sealed class WorkforceScheduleTenantIntegrityTests
{
    [Fact]
    public void WorkforceEndpointsEnforcePermissionTenantBranchAtomicityAndBoundedInput()
    {
        var source = ReadSource("backend-dotnet", "Controllers", "EndpointMappings.cs");
        var start = source.IndexOf("// ===== WORKFORCE MANAGEMENT", StringComparison.Ordinal);
        var end = source.IndexOf("app.MapGet(\"/api/cost-margin/recommendations\"", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var section = source[start..end];

        Assert.Equal(2, Count(section, "RequirePermission(http, \"dispatch:view\")"));
        Assert.Contains("RequirePermission(http, \"dispatch:assign\")", section, StringComparison.Ordinal);
        Assert.Equal(2, Count(section, "StrictBranchFilter(http, \"d\")"));
        Assert.Contains("WHERE ws.company_id=@cid", section, StringComparison.Ordinal);
        Assert.Equal(2, Count(section, "d.deleted_at IS NULL"));
        Assert.Contains("d.company_id=ws.company_id", section, StringComparison.Ordinal);
        Assert.Contains("ws.driver_id AS driver_id", section, StringComparison.Ordinal);
        Assert.Contains("d.full_name AS driver_name", section, StringComparison.Ordinal);
        Assert.Contains("ws.monday AS mon", section, StringComparison.Ordinal);
        // DEF-015: the workforce projection must carry NO license material. The old shape
        // exposed the license NUMBER mislabeled as a licence CLASS (drivers has no
        // license_class column). The response key survives — empty, never fabricated.
        Assert.Contains("'' AS licence_class", section, StringComparison.Ordinal);
        Assert.DoesNotContain("license_number", section, StringComparison.Ordinal);
        Assert.Contains("0 AS hours_this_week", section, StringComparison.Ordinal);
        Assert.Contains("70 AS hos_limit", section, StringComparison.Ordinal);
        Assert.Contains("AS safety_score", section, StringComparison.Ordinal);
        Assert.Contains("RunInTenantTransactionAsync(companyId", section, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", section, StringComparison.Ordinal);
        Assert.Contains("company_id=@companyId AND deleted_at IS NULL", section, StringComparison.Ordinal);
        Assert.Contains("branch_id=@branchId", section, StringComparison.Ordinal);
        Assert.Contains("company_id, branch_id, driver_id", section, StringComparison.Ordinal);
        Assert.Contains("branch_id=EXCLUDED.branch_id", section, StringComparison.Ordinal);
        Assert.Contains("NpgsqlParameter(\"@driverBranchId\", NpgsqlDbType.Bigint)", section, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT (company_id, driver_id, week_start)", section, StringComparison.Ordinal);
        Assert.Contains("DATE_TRUNC('week',CURRENT_DATE)::date", section, StringComparison.Ordinal);
        Assert.Contains("req.Day.Length>3", section, StringComparison.Ordinal);
        Assert.Contains("req.Shift.Length>40", section, StringComparison.Ordinal);
        Assert.Contains("\"Rest (HOS)\"", section, StringComparison.Ordinal);
        Assert.DoesNotContain("@branchId::BIGINT IS NULL", section, StringComparison.Ordinal);
        Assert.True(section.IndexOf("Driver not found", StringComparison.Ordinal)
                    < section.IndexOf("audit.LogAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void WorkforceUiIsReadOnlyWithoutTheExactWritePermissionAndSurfacesMutationFailures()
    {
        var source = ReadSource("frontend", "src", "pages", "WorkforceManagementPage.tsx");
        Assert.Contains("useHasPermission", source, StringComparison.Ordinal);
        Assert.Contains("hasPermission(\"dispatch:assign\")", source, StringComparison.Ordinal);
        Assert.Contains("disabled={!canAssign || assign.isPending}", source, StringComparison.Ordinal);
        Assert.Contains("Read-only access — dispatch assignment permission is required", source, StringComparison.Ordinal);
        Assert.Contains("assign.isError", source, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", source, StringComparison.Ordinal);
        Assert.Equal(2, Count(source, "row[day.toLowerCase()]"));
    }

    [Fact]
    public void Stage57IsCleanDeployCapableAndVerifiesExactSecurityAndIntegrityContracts()
    {
        var migration = ReadSource("database", "migrations", "2026_07_30_stage57_workforce_schedule_tenant_integrity.sql");
        var batch7 = ReadSource("backend-dotnet", "Services", "Batch7SchemaService.cs");

        Assert.Contains("CREATE TABLE IF NOT EXISTS workforce_schedules", migration, StringComparison.Ordinal);
        Assert.Contains("SET company_id=d.company_id", migration, StringComparison.Ordinal);
        Assert.Contains("branch_id=d.branch_id", migration, StringComparison.Ordinal);
        Assert.Contains("orphan workforce schedule", migration, StringComparison.Ordinal);
        Assert.Contains("duplicate tenant/driver/week", migration, StringComparison.Ordinal);
        Assert.Contains("ALTER COLUMN company_id SET NOT NULL", migration, StringComparison.Ordinal);
        Assert.Contains("FOREIGN KEY(company_id,driver_id) REFERENCES drivers(company_id,id)", migration, StringComparison.Ordinal);
        Assert.Contains("DROP INDEX IF EXISTS uq_workforce_schedules_tenant_driver_week", migration, StringComparison.Ordinal);
        Assert.Contains("relrowsecurity AND relforcerowsecurity", migration, StringComparison.Ordinal);
        Assert.Contains("permissive='PERMISSIVE'", migration, StringComparison.Ordinal);
        Assert.Contains("roles='{public}'::name[]", migration, StringComparison.Ordinal);
        Assert.Contains("GRANT SELECT,INSERT,UPDATE ON TABLE workforce_schedules", migration, StringComparison.Ordinal);
        Assert.Contains("has_table_privilege('opstrax_app','workforce_schedules','DELETE')", migration, StringComparison.Ordinal);
        Assert.True(migration.IndexOf("$workforce_verify$", StringComparison.Ordinal)
                    < migration.IndexOf("INSERT INTO schema_migrations", StringComparison.Ordinal));
        Assert.Contains("company_id BIGINT NOT NULL REFERENCES companies(id)", batch7, StringComparison.Ordinal);
        Assert.Contains("branch_id BIGINT NULL", batch7, StringComparison.Ordinal);
        Assert.Contains("UNIQUE (company_id, driver_id, week_start)", batch7, StringComparison.Ordinal);
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        for (var index = 0; (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length)
            count++;
        return count;
    }

    private static string ReadSource(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend-dotnet")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!.FullName, .. parts]));
    }
}

public sealed class WorkforceScheduleTenantIntegrityPostgresTests
{
    [Fact]
    public async Task MigrationRejectsBadHistoryThenPreservesRowsAndRlsUnderConcurrentTenantWrites()
    {
        var adminBuilder = new NpgsqlConnectionStringBuilder(TestDb.ConnectionString)
        {
            Database = "postgres",
            Pooling = false,
        };
        var databaseName = $"opstrax_stage57_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await admin.OpenAsync();
            await new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin).ExecuteNonQueryAsync();
        }

        var ownerBuilder = new NpgsqlConnectionStringBuilder(TestDb.ConnectionString)
        {
            Database = databaseName,
            Pooling = false,
        };
        var appBuilder = new NpgsqlConnectionStringBuilder(TestDb.AppConnectionString)
        {
            Database = databaseName,
            Pooling = false,
        };
        var migration = await File.ReadAllTextAsync(MigrationPath());

        try
        {
            await using var owner = new NpgsqlConnection(ownerBuilder.ConnectionString);
            await owner.OpenAsync();
            await Execute(owner, @"
CREATE TABLE drivers(id bigint PRIMARY KEY,company_id bigint NOT NULL,branch_id bigint NULL,full_name text NOT NULL,deleted_at timestamptz NULL);
CREATE TABLE schema_migrations(version text PRIMARY KEY,description text NOT NULL);
CREATE TABLE workforce_schedules(
 id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,driver_id bigint NOT NULL,week_start date NOT NULL,
 monday varchar(40) NOT NULL DEFAULT 'Off',tuesday varchar(40) NOT NULL DEFAULT 'Off',wednesday varchar(40) NOT NULL DEFAULT 'Off',
 thursday varchar(40) NOT NULL DEFAULT 'Off',friday varchar(40) NOT NULL DEFAULT 'Off',saturday varchar(40) NOT NULL DEFAULT 'Off',
 sunday varchar(40) NOT NULL DEFAULT 'Off',created_at timestamptz NOT NULL DEFAULT now(),updated_at timestamptz NULL);
INSERT INTO drivers VALUES(101,11,111,'Tenant A Driver',NULL),(202,22,222,'Tenant B Driver',NULL),(303,11,NULL,'Unbranched Driver',NULL);
INSERT INTO workforce_schedules(driver_id,week_start,monday) VALUES
 (101,date_trunc('week',current_date),'Morning'),(202,date_trunc('week',current_date),'Night'),
 (999,date_trunc('week',current_date),'Off');");

            var orphan = await Assert.ThrowsAsync<PostgresException>(() => Execute(owner, migration));
            Assert.Contains("orphan workforce schedule", orphan.MessageText, StringComparison.OrdinalIgnoreCase);
            await Execute(owner, "ROLLBACK");
            Assert.Equal(0L, await Scalar(owner, "SELECT COUNT(*) FROM information_schema.columns WHERE table_name='workforce_schedules' AND column_name='company_id'"));

            await Execute(owner, "DELETE FROM workforce_schedules WHERE driver_id=999; INSERT INTO workforce_schedules(driver_id,week_start,tuesday) VALUES(101,date_trunc('week',current_date),'Afternoon');");
            var duplicate = await Assert.ThrowsAsync<PostgresException>(() => Execute(owner, migration));
            Assert.Contains("duplicate tenant/driver/week", duplicate.MessageText, StringComparison.OrdinalIgnoreCase);
            await Execute(owner, "ROLLBACK");
            await Execute(owner, "DELETE FROM workforce_schedules WHERE driver_id=101 AND tuesday='Afternoon';");

            await Execute(owner, migration);
            await Execute(owner, migration); // exact idempotence
            Assert.Equal(2L, await Scalar(owner, "SELECT COUNT(*) FROM workforce_schedules WHERE (company_id,branch_id,driver_id) IN ((11,111,101),(22,222,202))"));
            Assert.Equal(2L, await Scalar(owner, "SELECT COUNT(*) FROM pg_policies WHERE tablename='workforce_schedules'"));

            var apiDb = new Database(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string,string?> { ["ConnectionStrings:DefaultConnection"]=ownerBuilder.ConnectionString }).Build());
            var responseRows = await apiDb.QueryAsync(@"SELECT ws.driver_id AS driver_id,d.full_name AS driver_name,
ws.monday AS mon,ws.tuesday AS tue,ws.wednesday AS wed,ws.thursday AS thu,ws.friday AS fri,ws.saturday AS sat,ws.sunday AS sun
FROM workforce_schedules ws JOIN drivers d ON d.id=ws.driver_id AND d.company_id=ws.company_id ORDER BY ws.driver_id");
            Assert.Equal(["driverId","driverName","mon","tue","wed","thu","fri","sat","sun"], responseRows[0].Keys.ToArray());
            Assert.Equal("Morning", responseRows[0]["mon"]);
            var rosterShape = (await apiDb.QueryAsync(@"SELECT 'C' AS licence_class,0 AS hours_this_week,
70 AS hos_limit,95 AS safety_score"))[0];
            Assert.Equal(["licenceClass","hoursThisWeek","hosLimit","safetyScore"], rosterShape.Keys.ToArray());
            Assert.Equal(95, rosterShape["safetyScore"]);
            var mismatchedDriver = await Assert.ThrowsAsync<PostgresException>(() => Execute(owner,
                "INSERT INTO workforce_schedules(company_id,driver_id,week_start) VALUES(11,202,current_date)"));
            Assert.Equal("23503", mismatchedDriver.SqlState);

            // Tenant-wide scheduling of an unbranched driver binds SQL NULL with an
            // explicit bigint type; this is the live Npgsql path that previously threw
            // before PostgreSQL could execute the write.
            await using (var tenantAdmin = new NpgsqlConnection(appBuilder.ConnectionString))
            {
                await tenantAdmin.OpenAsync();
                await using var tx = await tenantAdmin.BeginTransactionAsync();
                await Execute(tenantAdmin, "SELECT set_config('app.current_tenant_id','11',true)", tx);
                await using var command = new NpgsqlCommand(@"INSERT INTO workforce_schedules(company_id,branch_id,driver_id,week_start,monday)
VALUES(11,@branchId,303,date_trunc('week',current_date),'Morning')", tenantAdmin, tx);
                command.Parameters.Add(new NpgsqlParameter("@branchId", NpgsqlTypes.NpgsqlDbType.Bigint) { Value=DBNull.Value });
                Assert.Equal(1, await command.ExecuteNonQueryAsync());
                await tx.CommitAsync();
            }
            Assert.Equal(1L, await Scalar(owner, "SELECT COUNT(*) FROM workforce_schedules WHERE company_id=11 AND driver_id=303 AND branch_id IS NULL"));

            await using (var tenantA = new NpgsqlConnection(appBuilder.ConnectionString))
            {
                await tenantA.OpenAsync();
                await using var tx = await tenantA.BeginTransactionAsync();
                await Execute(tenantA, "SELECT set_config('app.current_tenant_id','11',true)", tx);
                Assert.Equal(2L, await Scalar(tenantA, "SELECT COUNT(*) FROM workforce_schedules", tx));
                var foreignWrite = await Assert.ThrowsAsync<PostgresException>(() => Execute(tenantA,
                    "INSERT INTO workforce_schedules(company_id,branch_id,driver_id,week_start) VALUES(22,222,202,current_date)", tx));
                Assert.Equal("42501", foreignWrite.SqlState);
                await tx.RollbackAsync();
            }

            var tasks = Enumerable.Range(0, 24).Select(async i =>
            {
                await using var app = new NpgsqlConnection(appBuilder.ConnectionString);
                await app.OpenAsync();
                await using var tx = await app.BeginTransactionAsync();
                await Execute(app, "SELECT set_config('app.current_tenant_id','11',true)", tx);
                var day = i % 2 == 0 ? "wednesday" : "thursday";
                var shift = i % 2 == 0 ? "Morning" : "Night";
                await Execute(app, $@"INSERT INTO workforce_schedules(company_id,branch_id,driver_id,week_start,{day})
VALUES(11,111,101,date_trunc('week',current_date),'{shift}')
ON CONFLICT(company_id,driver_id,week_start) DO UPDATE SET {day}=EXCLUDED.{day},updated_at=now()", tx);
                await tx.CommitAsync();
            });
            await Task.WhenAll(tasks);
            Assert.Equal(1L, await Scalar(owner, "SELECT COUNT(*) FROM workforce_schedules WHERE company_id=11 AND driver_id=101 AND week_start=date_trunc('week',current_date)"));
            Assert.Equal(1L, await Scalar(owner, "SELECT COUNT(*) FROM workforce_schedules WHERE company_id=11 AND driver_id=101 AND wednesday='Morning' AND thursday='Night'"));

            await Execute(owner, "CREATE POLICY rogue_allow_all ON workforce_schedules USING(true) WITH CHECK(true); GRANT DELETE ON workforce_schedules TO opstrax_app;");
            await Execute(owner, migration);
            Assert.Equal(2L, await Scalar(owner, "SELECT COUNT(*) FROM pg_policies WHERE tablename='workforce_schedules'"));
            Assert.Equal(0L, await Scalar(owner, "SELECT has_table_privilege('opstrax_app','workforce_schedules','DELETE')::int"));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(adminBuilder.ConnectionString);
            await admin.OpenAsync();
            await new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", admin).ExecuteNonQueryAsync();
        }
    }

    private static async Task Execute(NpgsqlConnection connection, string sql, NpgsqlTransaction? transaction = null)
        => await new NpgsqlCommand(sql, connection, transaction).ExecuteNonQueryAsync();

    private static async Task<long> Scalar(NpgsqlConnection connection, string sql, NpgsqlTransaction? transaction = null)
        => Convert.ToInt64(await new NpgsqlCommand(sql, connection, transaction).ExecuteScalarAsync());

    private static string MigrationPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "database"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "database", "migrations", "2026_07_30_stage57_workforce_schedule_tenant_integrity.sql");
    }
}
