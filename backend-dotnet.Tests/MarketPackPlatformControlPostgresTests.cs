using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

[Collection("platform-control-plane")]
[Trait("Category", "Integration")]
public sealed class MarketPackPlatformControlPostgresTests
{
    [Fact]
    public async Task Mutation_IsAuthorizedValidatedAtomicAndPlatformAudited()
    {
        var db = Db();
        await new PlatformSchemaService(db).EnsureAsync();
        await new RevenueSchemaService(db).EnsureAsync();
        await new MarketPackSchemaService(db).EnsureAsync();

        var suffix = Guid.NewGuid().ToString("N")[..10];
        var companyId = await db.InsertAsync(
            "INSERT INTO companies (company_code,name,industry,status) VALUES (@code,@name,'Logistics','Active') RETURNING id",
            c => { c.Parameters.AddWithValue("@code", $"MP-{suffix}"); c.Parameters.AddWithValue("@name", $"Market Pack Test {suffix}"); });
        var manager = await SeedAdminAsync(db, "finance_admin", $"mp-manager-{suffix}@opstrax.test");
        var viewer = await SeedAdminAsync(db, "readonly_executive", $"mp-viewer-{suffix}@opstrax.test");

        try
        {
            var forbidden = await MarketPackEndpoints.PlatformSetTenantMarketPack(companyId, Http(viewer.Token),
                new() { ["packCode"] = MarketPackSchemaService.Packs.CanadaNa, ["status"] = "active" }, db, CancellationToken.None);
            Assert.Equal(StatusCodes.Status403Forbidden, Status(forbidden));
            Assert.Equal(0, await AssignmentCount(db, companyId));

            foreach (var invalidStatus in new[] { "enabled", "ACTIVE", "trial", " " })
            {
                // Blank remains the documented backwards-compatible default of active;
                // the other free-form states must be rejected.
                if (string.IsNullOrWhiteSpace(invalidStatus)) continue;
                var rejected = await MarketPackEndpoints.PlatformSetTenantMarketPack(companyId, Http(manager.Token),
                    new() { ["packCode"] = MarketPackSchemaService.Packs.CanadaNa, ["status"] = invalidStatus }, db, CancellationToken.None);
                Assert.Equal(StatusCodes.Status400BadRequest, Status(rejected));
            }
            Assert.Equal(0, await AssignmentCount(db, companyId));

            var unknown = await MarketPackEndpoints.PlatformSetTenantMarketPack(companyId, Http(manager.Token),
                new() { ["packCode"] = "unknown_pack", ["status"] = "active" }, db, CancellationToken.None);
            Assert.Equal(StatusCodes.Status404NotFound, Status(unknown));
            Assert.Equal(0, await AssignmentCount(db, companyId));

            var unknownTenant = await MarketPackEndpoints.PlatformSetTenantMarketPack(long.MaxValue, Http(manager.Token),
                new() { ["packCode"] = MarketPackSchemaService.Packs.CanadaNa, ["status"] = "active" }, db, CancellationToken.None);
            Assert.Equal(StatusCodes.Status404NotFound, Status(unknownTenant));

            var invalidPrice = await MarketPackEndpoints.PlatformSetTenantMarketPack(companyId, Http(manager.Token),
                new() { ["packCode"] = MarketPackSchemaService.Packs.CanadaNa, ["status"] = "active", ["priceOverrideCents"] = -1 }, db, CancellationToken.None);
            Assert.Equal(StatusCodes.Status400BadRequest, Status(invalidPrice));

            var enabled = await MarketPackEndpoints.PlatformSetTenantMarketPack(companyId, Http(manager.Token),
                new()
                {
                    ["packCode"] = MarketPackSchemaService.Packs.CanadaNa,
                    ["status"] = "active",
                    ["priceOverrideCents"] = 42000,
                    ["reason"] = "Approved Canada pilot add-on",
                }, db, CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Status(enabled));
            var activeRow = await db.QuerySingleAsync(
                "SELECT status FROM tenant_market_packs WHERE company_id=@c AND pack_code='canada_na'",
                c => c.Parameters.AddWithValue("@c", companyId));
            Assert.Equal("active", activeRow?["status"]?.ToString());
            Assert.Equal(1, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM tenant_entitlements WHERE company_id=@c AND module_key='market.canada_na' AND enabled=true AND source='market_pack'",
                c => c.Parameters.AddWithValue("@c", companyId)));

            var disabled = await MarketPackEndpoints.PlatformSetTenantMarketPack(companyId, Http(manager.Token),
                new()
                {
                    ["packCode"] = MarketPackSchemaService.Packs.CanadaNa,
                    ["status"] = "disabled",
                    ["reason"] = "Pilot term ended",
                }, db, CancellationToken.None);
            Assert.Equal(StatusCodes.Status200OK, Status(disabled));
            Assert.Equal(1, await db.ScalarLongAsync(
                "SELECT COUNT(*) FROM tenant_entitlements WHERE company_id=@c AND module_key='market.canada_na' AND enabled=false AND source='market_pack'",
                c => c.Parameters.AddWithValue("@c", companyId)));

            var audits = await db.QueryAsync(
                "SELECT actor_admin_id,actor_email,action,entity_type,target_company_id,details_json FROM platform_audit_log WHERE target_company_id=@c AND action='tenant.market_pack.changed' ORDER BY id",
                c => c.Parameters.AddWithValue("@c", companyId));
            Assert.Equal(2, audits.Count);
            Assert.All(audits, row =>
            {
                Assert.Equal(manager.AdminId, Convert.ToInt64(row["actorAdminId"]));
                Assert.Equal(manager.Email, row["actorEmail"]?.ToString());
                Assert.Equal("MarketPackAssignment", row["entityType"]?.ToString());
            });
            var firstAudit = audits[0]["detailsJson"]?.ToString() ?? "";
            var secondAudit = audits[1]["detailsJson"]?.ToString() ?? "";
            Assert.Contains("Approved Canada pilot add-on", firstAudit, StringComparison.Ordinal);
            Assert.Contains("\"before\"", secondAudit, StringComparison.Ordinal);
            Assert.Contains("\"after\"", secondAudit, StringComparison.Ordinal);
            Assert.Contains("Pilot term ended", secondAudit, StringComparison.Ordinal);
            Assert.Contains("active", secondAudit, StringComparison.Ordinal);
            Assert.Contains("disabled", secondAudit, StringComparison.Ordinal);

            // The database is a second enforcement layer for non-HTTP writers.
            await Assert.ThrowsAnyAsync<Exception>(() => db.ExecuteAsync(
                "UPDATE tenant_market_packs SET status='invented' WHERE company_id=@c",
                c => c.Parameters.AddWithValue("@c", companyId)));
        }
        finally
        {
            foreach (var sql in new[]
            {
                "DELETE FROM platform_audit_log WHERE target_company_id=@c OR actor_admin_id IN (@manager,@viewer)",
                "DELETE FROM usage_events WHERE company_id=@c",
                "DELETE FROM usage_counters WHERE company_id=@c",
                "DELETE FROM tenant_entitlements WHERE company_id=@c",
                "DELETE FROM tenant_market_packs WHERE company_id=@c",
                "DELETE FROM platform_sessions WHERE admin_id IN (@manager,@viewer)",
                "DELETE FROM platform_admins WHERE id IN (@manager,@viewer)",
                "DELETE FROM companies WHERE id=@c",
            })
                await db.ExecuteAsync(sql, c =>
                {
                    c.Parameters.AddWithValue("@c", companyId);
                    c.Parameters.AddWithValue("@manager", manager.AdminId);
                    c.Parameters.AddWithValue("@viewer", viewer.AdminId);
                });
        }
    }

    private static Database Db() => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = TestDb.ConnectionString }).Build());

    private static DefaultHttpContext Http(string token)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = $"Bearer {token}";
        http.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        return http;
    }

    private static int Status(IResult result) => (result as IStatusCodeHttpResult)?.StatusCode ?? StatusCodes.Status200OK;
    private static Task<long> AssignmentCount(Database db, long companyId) => db.ScalarLongAsync(
        "SELECT COUNT(*) FROM tenant_market_packs WHERE company_id=@c", c => c.Parameters.AddWithValue("@c", companyId));

    private static async Task<(long AdminId, string Token, string Email)> SeedAdminAsync(Database db, string roleKey, string email)
    {
        var roleId = await db.ScalarLongAsync("SELECT id FROM platform_roles WHERE role_key=@key", c => c.Parameters.AddWithValue("@key", roleKey));
        var adminId = await db.InsertAsync(
            "INSERT INTO platform_admins(email,full_name,password_hash,role_id,status) VALUES (@email,'Market Pack Control Test',@hash,@role,'Active') RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@email", email);
                c.Parameters.AddWithValue("@hash", PlatformSchemaService.HashPassword("Market-Pack-Test-1!"));
                c.Parameters.AddWithValue("@role", roleId);
            });
        var token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        await db.ExecuteAsync("INSERT INTO platform_sessions(admin_id,session_token,expires_at) VALUES (@admin,@token,NOW()+INTERVAL '1 hour')",
            c => { c.Parameters.AddWithValue("@admin", adminId); c.Parameters.AddWithValue("@token", token); });
        return (adminId, token, email);
    }
}
