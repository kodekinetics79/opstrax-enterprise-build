using Opstrax.Api.Services;
using Opstrax.Api.DTOs;

namespace Opstrax.Api.Controllers;

public static class RatingEndpoints
{
    public static void MapRatingEndpoints(this WebApplication app)
    {
        // Compute a job's charges from its contracted rate card. mode=preview returns the computed
        // lines without writing; mode=commit (default) persists them as source='rating' (idempotent).
        app.MapPost("/api/jobs/{id:long}/rate", RateJob);
    }

    private static async Task<IResult> RateJob(
        HttpContext http, long id, System.Text.Json.JsonElement body, RatingService rating, CancellationToken ct)
    {
        if (EndpointMappings.RequirePermission(http, "charge.create") is { } denied) return denied;
        var companyId = EndpointMappings.GetCompanyId(http);

        var mode = RateMode.Commit;
        if (body.ValueKind == System.Text.Json.JsonValueKind.Object &&
            body.TryGetProperty("mode", out var m) && m.ValueKind == System.Text.Json.JsonValueKind.String &&
            string.Equals(m.GetString(), "preview", StringComparison.OrdinalIgnoreCase))
            mode = RateMode.Preview;

        var outcome = await rating.RateJobAsync(companyId, id, mode, ct);

        // A resolvable job that simply can't be auto-priced (no rate card / missing distance) is not
        // an error — it's an honest "unpriced" result the caller surfaces (the leakage signal stands).
        if (!outcome.Success)
            return Results.Conflict(ApiResponse<object>.Fail(outcome.Message));
        return Results.Ok(ApiResponse<object>.Ok(new
        {
            jobId = outcome.JobId,
            priced = outcome.Priced,
            unpricedReason = outcome.UnpricedReason,
            mode = mode.ToString().ToLowerInvariant(),
            currency = outcome.Currency,
            total = outcome.Total,
            lines = outcome.Lines,
        }, outcome.Message));
    }
}
