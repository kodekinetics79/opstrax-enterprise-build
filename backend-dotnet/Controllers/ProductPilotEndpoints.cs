using System.Security.Cryptography;
using System.Text;
using Opstrax.Api.Data;
using Opstrax.Api.DTOs;

namespace Opstrax.Api.Controllers;

/// <summary>
/// Narrow staging control used to prepare the isolated customer-pilot tenant.
/// It can enable CRM for one fixed tenant. It cannot create tenant data, seed
/// fixtures, impersonate users, or invoke telemetry.
/// </summary>
public static class ProductPilotEndpoints
{
    internal const string CertificationTenantCode = "CERT-LARGE-20260825";
    private const string Permission = "platform:pilot:run";
    private const string Plan = "CERT-LARGE-20260825|entitlement:crm=true|v1";

    internal static bool IsAvailable(IHostEnvironment environment, IConfiguration configuration)
        => environment.IsStaging()
           && configuration.GetValue<bool>("ProductPilot:Enabled")
           && string.Equals(configuration["ProductPilot:DeploymentStage"], "staging", StringComparison.OrdinalIgnoreCase)
           && string.Equals(configuration["ProductPilot:TenantCode"], CertificationTenantCode, StringComparison.Ordinal);

    public static void Map(WebApplication app)
    {
        if (!IsAvailable(app.Environment, app.Configuration)) return;
        app.MapGet("/api/platform/product-pilot", Status);
        app.MapPost("/api/platform/product-pilot/enable-crm", EnableCrm);
    }

    private static IResult Unavailable()
        => Results.Json(ApiResponse<object>.Fail("Not found"), statusCode: StatusCodes.Status404NotFound);

    private static async Task<IResult> Status(HttpContext http, Database db, IHostEnvironment environment, IConfiguration configuration, CancellationToken ct)
    {
        if (!IsAvailable(environment, configuration)) return Unavailable();
        var (_, error) = await PlatformEndpoints.RequireAsync(http, db, Permission, ct);
        if (error is not null) return error;

        var tenant = await db.RunInSystemScopeAsync(() => db.QuerySingleAsync(
            @"SELECT c.id, c.company_code, c.name, c.status, c.entitlement_policy_mode,
                     CASE WHEN c.entitlement_policy_mode='package_allowlist'
                          THEN COALESCE((SELECT enabled FROM tenant_entitlements WHERE company_id=c.id AND module_key='crm'), false)
                          ELSE true END crm_enabled,
                     COALESCE((SELECT enabled FROM tenant_entitlements WHERE company_id=c.id AND module_key='dispatch'), false) dispatch_enabled,
                     (SELECT COUNT(*) FROM customers WHERE company_id=c.id AND deleted_at IS NULL) customer_count,
                     (SELECT COUNT(*) FROM jobs WHERE company_id=c.id AND deleted_at IS NULL) job_count,
                     (SELECT COUNT(*) FROM routes WHERE company_id=c.id AND deleted_at IS NULL) route_count
              FROM companies c WHERE c.company_code=@code LIMIT 1",
            c => c.Parameters.AddWithValue("@code", CertificationTenantCode), ct), ct);

        if (tenant is null)
            return Results.Json(ApiResponse<object>.Fail("Certification tenant is unavailable"), statusCode: StatusCodes.Status409Conflict);

        var policy = tenant["entitlementPolicyMode"]?.ToString() ?? "unknown";
        var active = string.Equals(tenant["status"]?.ToString(), "Active", StringComparison.OrdinalIgnoreCase);
        var eligible = active && string.Equals(policy, "package_allowlist", StringComparison.Ordinal);
        var deployedSha = configuration["OPSTRAX_DEPLOY_VERSION"] ?? configuration["RENDER_GIT_COMMIT"] ?? "unknown";

        return Results.Ok(ApiResponse<object>.Ok(new
        {
            environment = "staging",
            deployedSha,
            eligible,
            tenant = new
            {
                id = Convert.ToInt64(tenant["id"]),
                code = tenant["companyCode"],
                name = tenant["name"],
                status = tenant["status"],
                entitlementPolicy = policy,
            },
            entitlements = new { crm = tenant["crmEnabled"], dispatch = tenant["dispatchEnabled"] },
            records = new { customers = tenant["customerCount"], jobs = tenant["jobCount"], routes = tenant["routeCount"] },
            workflow = new
            {
                customer = "/customers",
                jobs = "/jobs",
                routes = "/route-planning",
                instruction = "Sign in separately as the certification tenant administrator. Create all records through these customer-facing workflows."
            },
        }, "Product pilot readiness"));
    }

    internal sealed record EnableCrmRequest(string TenantCode, Guid RequestId, bool AcknowledgeStagingOnly);
    internal sealed record PilotRunResult(Guid RequestId, bool Replayed, string Status, bool CrmBefore, bool CrmAfter, DateTimeOffset? CompletedAt);

    private static async Task<IResult> EnableCrm(
        HttpContext http,
        Database db,
        IHostEnvironment environment,
        IConfiguration configuration,
        EnableCrmRequest request,
        CancellationToken ct)
    {
        if (!IsAvailable(environment, configuration)) return Unavailable();
        var (principal, error) = await PlatformEndpoints.RequireAsync(http, db, Permission, ct);
        if (error is not null) return error;
        if (!request.AcknowledgeStagingOnly || !string.Equals(request.TenantCode, CertificationTenantCode, StringComparison.Ordinal))
            return Results.Json(ApiResponse<object>.Fail("Confirmation required", $"Type {CertificationTenantCode} and acknowledge staging-only execution."), statusCode: StatusCodes.Status400BadRequest);
        if (request.RequestId == Guid.Empty)
            return Results.Json(ApiResponse<object>.Fail("Request ID required", "Generate one request ID and reuse it only when retrying this exact action."), statusCode: StatusCodes.Status400BadRequest);

        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Plan))).ToLowerInvariant();
        try
        {
            var result = await db.RunInSystemTransactionAsync<PilotRunResult>(async () =>
            {
                // Serialize the durable audit-ledger idempotency key. The CRM upsert
                // and its audit record remain in this same system transaction.
                await db.ScalarLongAsync("SELECT 1 FROM (SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))) AS locked",
                    c => c.Parameters.AddWithValue("@key", request.RequestId.ToString("D")), ct);
                var prior = await db.QuerySingleAsync(
                    @"SELECT details_json->>'planFingerprint' plan_fingerprint,
                             details_json->>'crmBefore' crm_before,
                             details_json->>'crmAfter' crm_after,
                             created_at completed_at
                      FROM platform_audit_log
                      WHERE action LIKE 'product_pilot.crm.%'
                        AND details_json->>'requestId'=@requestId
                      ORDER BY id DESC LIMIT 1",
                    c => c.Parameters.AddWithValue("@requestId", request.RequestId.ToString("D")), ct);
                if (prior is not null)
                {
                    if (!string.Equals(prior["planFingerprint"]?.ToString(), fingerprint, StringComparison.Ordinal))
                        throw new PilotConflictException("The request ID is already bound to a different pilot action.");
                    return new PilotRunResult(
                        request.RequestId,
                        true,
                        "completed",
                        Convert.ToBoolean(prior["crmBefore"]),
                        Convert.ToBoolean(prior["crmAfter"]),
                        ToDateTimeOffset(prior["completedAt"]));
                }

                var tenant = await db.QuerySingleAsync(
                    @"SELECT id, company_code, status, entitlement_policy_mode FROM companies
                      WHERE company_code=@code FOR UPDATE",
                    c => c.Parameters.AddWithValue("@code", CertificationTenantCode), ct);
                if (tenant is null) throw new PilotConflictException("The certification tenant does not exist.");
                if (!string.Equals(tenant["companyCode"]?.ToString(), CertificationTenantCode, StringComparison.Ordinal)
                    || !string.Equals(tenant["status"]?.ToString(), "Active", StringComparison.OrdinalIgnoreCase))
                    throw new PilotConflictException("The certification tenant identity or status is not eligible.");
                if (!string.Equals(tenant["entitlementPolicyMode"]?.ToString(), "package_allowlist", StringComparison.Ordinal))
                    throw new PilotConflictException("The certification tenant must use the package_allowlist entitlement policy before pilot activation.");

                var companyId = Convert.ToInt64(tenant["id"]);
                var before = await db.ScalarLongAsync(
                    "SELECT COUNT(*) FROM tenant_entitlements WHERE company_id=@companyId AND module_key='crm' AND enabled=true",
                    c => c.Parameters.AddWithValue("@companyId", companyId), ct) > 0;

                await db.ExecuteAsync(
                    @"INSERT INTO tenant_entitlements(company_id,module_key,enabled,tier,source,updated_by,updated_at)
                      VALUES (@companyId,'crm',true,'standard','override',@actor,NOW())
                      ON CONFLICT(company_id,module_key) DO UPDATE
                      SET enabled=true, source='override', updated_by=EXCLUDED.updated_by, updated_at=NOW()",
                    c =>
                    {
                        c.Parameters.AddWithValue("@companyId", companyId);
                        c.Parameters.AddWithValue("@actor", principal!.Email);
                    }, ct);

                await PlatformEndpoints.AuditAsync(db, principal!, http,
                    before ? "product_pilot.crm.already_enabled" : "product_pilot.crm.enabled",
                    "ProductPilotRun", null, companyId,
                    new { requestId = request.RequestId, tenantCode = CertificationTenantCode, crmBefore = before, crmAfter = true, planFingerprint = fingerprint }, ct);

                return new PilotRunResult(request.RequestId, false, "completed", before, true, DateTimeOffset.UtcNow);
            }, ct);

            return Results.Ok(ApiResponse<object>.Ok(result, "CRM entitlement is ready. Continue in the tenant customer-facing workflow."));
        }
        catch (PilotConflictException ex)
        {
            return Results.Json(ApiResponse<object>.Fail("Pilot action rejected", ex.Message), statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static DateTimeOffset? ToDateTimeOffset(object? value) => value switch
    {
        DateTimeOffset offset => offset,
        DateTime dateTime => new DateTimeOffset(dateTime),
        _ => null,
    };

    private sealed class PilotConflictException(string message) : Exception(message);
}
