using Opstrax.Api.Data;
using Opstrax.Api.DTOs;
using Opstrax.Api.Services;

namespace Opstrax.Api.Controllers;

// Demo-tenant seeding endpoint. Triple-gated so it is impossible to trigger against
// a live production tenant:
//   1. The route is mapped ONLY when ASPNETCORE_ENVIRONMENT=Development.
//   2. Even in Development it requires an explicit config opt-in (DemoSeed:Enabled=true).
//   3. It rejects customer-portal principals (internal users only).
public static class DevSeedEndpoints
{
    public static void MapDevSeedEndpoints(this WebApplication app)
    {
        // Platform-authenticated destructive reset has its own stronger guard set.
        app.MapCanonicalDemoResetEndpoints();

        if (!app.Environment.IsDevelopment())
        {
            return; // never exposed in staging, production, or custom environments
        }

        app.MapPost("/api/dev/seed-demo-tenant", async (HttpContext http, IConfiguration config, DemoTenantSeeder seeder, CancellationToken ct) =>
        {
            if (!config.GetValue<bool>("DemoSeed:Enabled"))
            {
                return Results.NotFound(); // not opted in — behave as if the route does not exist
            }

            var denied = EndpointMappings.RequireInternalUser(http);
            if (denied is not null) return denied;

            var result = await seeder.SeedAsync(ct);
            return Results.Ok(ApiResponse<object>.Ok(result, result.Message));
        });

        // Backfill realistic ~90-day time-series (fuel, location breadcrumbs) onto an
        // EXISTING demo tenant so trend charts populate. Same triple-gating. Idempotent:
        // no-ops once history exists. Runs under a system scope to write RLS tables.
        app.MapPost("/api/dev/enrich-demo-timeseries", async (HttpContext http, IConfiguration config, DemoTenantSeeder seeder, Database db, CancellationToken ct) =>
        {
            if (!config.GetValue<bool>("DemoSeed:Enabled"))
            {
                return Results.NotFound();
            }

            var denied = EndpointMappings.RequireInternalUser(http);
            if (denied is not null) return denied;

            var companyId = EndpointMappings.GetCompanyId(http);
            var inserted = 0;
            await db.RunInSystemScopeAsync(async () =>
            {
                inserted = await seeder.EnrichTimeSeriesAsync(companyId, ct);
            }, ct);
            return Results.Ok(ApiResponse<object>.Ok(new { companyId, fuelTransactionsInserted = inserted },
                inserted > 0 ? $"Enriched tenant {companyId} with {inserted} fuel transactions + location history." : "History already present — no-op."));
        });
    }
}
