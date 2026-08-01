using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Opstrax.Api.Controllers;
using Opstrax.Api.Data;
using Opstrax.Api.Services;

namespace Opstrax.Tests;

public sealed class ComplianceEvidenceTenantBoundaryPostgresTests
{
    [Fact]
    public async Task GlobalAggregatesUseSystemLane_WhileEvidenceRemainsTenantOwned()
    {
        var owner = Db(TestDb.ConnectionString, false);
        var runtime = Db(TestDb.AppConnectionString, true, TestDb.SystemConnectionString);
        var suffix = Guid.NewGuid().ToString("N");
        var companyId = await owner.InsertAsync(
            "INSERT INTO companies(company_code,name,industry,status) VALUES(@code,@name,'Logistics','Active') RETURNING id",
            c =>
            {
                c.Parameters.AddWithValue("@code", $"CE-{suffix[..16]}".ToUpperInvariant());
                c.Parameters.AddWithValue("@name", $"Compliance Evidence {suffix[..8]}");
            });
        var userId = 8_800_000_000L + Random.Shared.Next(1, 500_000);
        var http = new DefaultHttpContext();
        http.Items[EndpointMappings.AuthCompanyIdItemKey] = companyId;
        http.Items[EndpointMappings.AuthUserIdItemKey] = userId;
        http.Items[EndpointMappings.AuthRoleItemKey] = "Company Admin";
        var service = new ComplianceService(runtime, new AuditService(runtime));

        try
        {
            await Assert.ThrowsAsync<PostgresException>(() => runtime.RunInTenantScopeAsync(companyId,
                () => runtime.ScalarLongAsync("SELECT COUNT(*) FROM service_run_history")));

            foreach (var evidenceType in new[] { "service_health", "backup_verification", "incident_resolution" })
            {
                var evidenceId = await runtime.RunInTenantScopeAsync(companyId,
                    () => service.GenerateEvidenceAsync(companyId, $"TEST-{suffix[..8]}", evidenceType, http));
                Assert.True(evidenceId > 0);
            }

            var visible = await runtime.RunInTenantScopeAsync(companyId, () => runtime.QueryAsync(
                @"SELECT company_id,evidence_type,source_record_id,generated_by
                  FROM compliance_evidence WHERE generated_by=@generated ORDER BY evidence_type",
                c => c.Parameters.AddWithValue("@generated", $"user:{userId}")));
            Assert.Equal(3, visible.Count);
            Assert.All(visible, row =>
            {
                Assert.Equal(companyId, Convert.ToInt64(row["companyId"]));
                Assert.Null(row["sourceRecordId"]);
                Assert.Equal($"user:{userId}", row["generatedBy"]);
            });
            Assert.Empty(await runtime.QueryAsync(
                "SELECT id FROM compliance_evidence WHERE generated_by=@generated",
                c => c.Parameters.AddWithValue("@generated", $"user:{userId}")));
        }
        finally
        {
            await owner.ExecuteAsync("DELETE FROM compliance_evidence WHERE company_id=@cid",
                c => c.Parameters.AddWithValue("@cid", companyId));
            await owner.ExecuteAsync("DELETE FROM audit_logs WHERE company_id=@cid",
                c => c.Parameters.AddWithValue("@cid", companyId));
            await owner.ExecuteAsync("DELETE FROM companies WHERE id=@cid",
                c => c.Parameters.AddWithValue("@cid", companyId));
        }
    }

    private static Database Db(string appConnection, bool rls, string? systemConnection = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = appConnection,
            ["ConnectionStrings:SystemConnection"] = systemConnection,
            ["Rls:EnforceTenantContext"] = rls.ToString(),
            ["Rls:TenantTicketTtlSeconds"] = "120",
        }).Build();
        return new Database(config, new TenantScopeAccessor());
    }
}
