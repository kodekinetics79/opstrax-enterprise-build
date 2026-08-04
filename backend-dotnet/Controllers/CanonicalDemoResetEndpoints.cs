using Opstrax.Api.Data;
using Opstrax.Api.DTOs;
using Opstrax.Api.Services;

namespace Opstrax.Api.Controllers;

/// <summary>
/// Destructive reset for the one canonical sales-demo tenant. This is deliberately
/// separate from the additive/idempotent demo seed endpoint: reset first offboards
/// the existing fixture, then recreates it from DemoTenantSeeder's versioned source.
/// </summary>
public static class CanonicalDemoResetEndpoints
{
    internal const string Route = "/api/platform/dev/reset-canonical-demo";
    internal const string Confirmation = "RESET MERIDIAN-DEMO";

    internal sealed record ResetRequest(string? Confirm, string? Reason);

    public static void MapCanonicalDemoResetEndpoints(this WebApplication app)
    {
        // Fail closed: the route does not exist in Staging, Production, or a custom
        // environment. The handler repeats this check to protect direct invocation.
        if (!app.Environment.IsDevelopment()) return;

        app.MapPost(Route, ResetCanonicalDemoAsync);
    }

    internal static bool IsEnabled(IHostEnvironment environment, IConfiguration configuration)
        => environment.IsDevelopment() && configuration.GetValue<bool>("DemoSeed:ResetEnabled");

    internal static async Task<IResult> ResetCanonicalDemoAsync(
        HttpContext http,
        ResetRequest body,
        IHostEnvironment environment,
        IConfiguration configuration,
        Database db,
        DemoTenantSeeder seeder,
        TenantOffboardingService offboarding,
        CancellationToken ct)
    {
        if (!IsEnabled(environment, configuration))
            return Results.NotFound();

        // The elevated offboarding grant is intentionally limited to platform super
        // admins today. Tenant sessions are never accepted on /api/platform/* paths.
        var (principal, error) = await PlatformEndpoints.RequireAsync(
            http, db, "platform:tenants:offboard", ct);
        if (error is not null) return error;

        if (!string.Equals(body.Confirm?.Trim(), Confirmation, StringComparison.Ordinal))
            return Results.Json(ApiResponse<object>.Fail(
                    "Confirmation required",
                    $"Send confirm exactly as '{Confirmation}'."),
                statusCode: StatusCodes.Status400BadRequest);

        var reason = body.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length < 8 || reason.Length > 500)
            return Results.Json(ApiResponse<object>.Fail(
                    "Validation failed", "reason must contain 8 to 500 characters"),
                statusCode: StatusCodes.Status400BadRequest);

        var tenant = await db.QuerySingleAsync(
            "SELECT id, name FROM companies WHERE company_code=@code LIMIT 1",
            c => c.Parameters.AddWithValue("@code", DemoTenantSeeder.DemoCompanyCode), ct);
        var oldCompanyId = tenant is null ? (long?)null : Convert.ToInt64(tenant["id"]);

        await PlatformEndpoints.AuditAsync(db, principal!, http,
            "demo.fixture.reset.started", "DemoFixture", oldCompanyId, oldCompanyId,
            new
            {
                companyCode = DemoTenantSeeder.DemoCompanyCode,
                fixtureVersion = DemoTenantSeeder.SafetyPilotFixtureVersion,
                reason,
            }, ct);

        TenantOffboardingService.OffboardResult? deletion = null;
        try
        {
            if (oldCompanyId.HasValue)
                deletion = await offboarding.DeleteTenantAsync(oldCompanyId.Value, ct);

            var seeded = await seeder.SeedAsync(ct);
            await PlatformEndpoints.AuditAsync(db, principal!, http,
                "demo.fixture.reset.completed", "DemoFixture", seeded.CompanyId, seeded.CompanyId,
                new
                {
                    companyCode = DemoTenantSeeder.DemoCompanyCode,
                    previousCompanyId = oldCompanyId,
                    fixtureVersion = DemoTenantSeeder.SafetyPilotFixtureVersion,
                    reason,
                    deletedRows = deletion?.TotalRowsDeleted ?? 0,
                    deletedTables = deletion?.DeletedByTable.Count ?? 0,
                    seeded.Vehicles,
                    seeded.Drivers,
                    seeded.Jobs,
                    seeded.IssuedInvoices,
                }, ct);

            return Results.Ok(ApiResponse<object>.Ok(new
            {
                companyCode = DemoTenantSeeder.DemoCompanyCode,
                previousCompanyId = oldCompanyId,
                companyId = seeded.CompanyId,
                fixtureVersion = DemoTenantSeeder.SafetyPilotFixtureVersion,
                deletedRows = deletion?.TotalRowsDeleted ?? 0,
                seeded.Vehicles,
                seeded.Drivers,
                seeded.Customers,
                seeded.Jobs,
                seeded.Trips,
                seeded.IssuedInvoices,
            }, "Canonical demo fixture reset completed"));
        }
        catch (Exception ex)
        {
            // Record the failed phase without leaking exception text or connection data.
            // If deletion committed before reseeding failed, this durable event makes the
            // incomplete state visible to operators and the same request is safe to retry.
            await PlatformEndpoints.AuditAsync(db, principal!, http,
                "demo.fixture.reset.failed", "DemoFixture", oldCompanyId, oldCompanyId,
                new
                {
                    companyCode = DemoTenantSeeder.DemoCompanyCode,
                    fixtureVersion = DemoTenantSeeder.SafetyPilotFixtureVersion,
                    reason,
                    phase = deletion is null ? "delete-or-seed" : "seed",
                    errorType = ex.GetType().Name,
                    // InvalidOperationException is the offboarding service's deliberate
                    // fail-closed residual-table diagnostic. It contains table names and
                    // counts, never credentials or connection details, and is essential
                    // for an operator to remediate a schema dependency without guessing.
                    failureDetail = ex is InvalidOperationException ? ex.Message : null,
                }, ct);
            throw;
        }
    }
}
