using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

public enum RateMode { Preview, Commit }

public sealed record ComputedCharge(
    string ChargeType, string ChargeCode, string ChargeName,
    decimal Quantity, decimal UnitRate, decimal Amount, string RateBasis, long RateCardId);

public sealed record RateJobOutcome(
    bool Success, string Message, bool Priced, long JobId,
    IReadOnlyList<ComputedCharge> Lines, decimal Total, string Currency, string? UnpricedReason);

// Configurable-rating Phase 1 (ADR-008 §A): computes a job's charges from its contracted rate card
// instead of the hand-keyed unit_rate/amount path, and writes them as source='rating' so re-rating
// is idempotent (delete-and-recompute only rating-owned, un-issued charges — a hand-keyed 'manual'
// charge or one already on an issued invoice is never touched). Fail-closed: no resolvable rate card
// -> Priced=false with ZERO writes; the existing job_without_contract_or_rate_card leakage signal
// stands. Supported bases now: flat / per_mile / per_km, + fuel surcharge % and a whole-charge
// minimum. Broader bases (per_stop/per_hour/tiered/accessorials) are the next phase.
public sealed class RatingService(Database db, BusinessSpineService spine)
{
    public async Task<RateJobOutcome> RateJobAsync(long companyId, long jobId, RateMode mode, CancellationToken ct = default)
    {
        var job = await db.QuerySingleAsync(
            @"SELECT id, company_id, rate_card_id, contract_id, required_vehicle_type,
                     pickup_latitude, pickup_longitude, dropoff_latitude, dropoff_longitude
              FROM jobs WHERE id=@id AND company_id=@cid AND deleted_at IS NULL",
            c => { c.Parameters.AddWithValue("@id", jobId); c.Parameters.AddWithValue("@cid", companyId); }, ct);
        if (job is null)
            return Unpriced(jobId, "job_not_found", "Job not found");

        var card = await ResolveRateCardAsync(companyId, job, ct);
        if (card is null)
            return Unpriced(jobId, "no_rate_card", "No rate card resolved for job — cannot auto-price");

        var basis = CanonicalBasis(card.BillingBasis);
        if (basis is null)
            return Unpriced(jobId, "unknown_billing_basis", $"Unsupported billing basis '{card.BillingBasis}'");

        // Base quantity by basis.
        decimal qty;
        switch (basis)
        {
            case "flat":
                qty = 1m;
                break;
            case "per_mile":
            case "per_km":
            {
                var miles = await ResolveMilesAsync(companyId, jobId, job, ct);
                if (miles is null)
                    return Unpriced(jobId, "missing_distance", "Per-distance rate card but no distance available for job");
                qty = basis == "per_km" ? Math.Round(miles.Value * 1.60934m, 3, MidpointRounding.AwayFromZero) : miles.Value;
                break;
            }
            case "per_stop":
            {
                var stops = await ResolveStopsAsync(companyId, jobId, ct);
                if (stops is null or 0)
                    return Unpriced(jobId, "missing_stops", "Per-stop rate card but no stops recorded for job");
                qty = stops.Value;
                break;
            }
            case "per_hour":
            {
                var hours = await ResolveHoursAsync(companyId, jobId, ct);
                if (hours is null or <= 0)
                    return Unpriced(jobId, "missing_duration", "Per-hour rate card but no duration recorded for job");
                qty = hours.Value;
                break;
            }
            default:
                return Unpriced(jobId, "unknown_billing_basis", $"Unsupported billing basis '{card.BillingBasis}'");
        }

        var lines = new List<ComputedCharge>();
        var baseAmount = Round2(qty * card.BaseRate);
        // Minimum applies to linehaul before surcharges (standard TMS).
        if (card.MinimumCharge is { } min && baseAmount < min)
            baseAmount = Round2(min);
        lines.Add(new ComputedCharge("base", "LINEHAUL", card.RateCardName is { Length: > 0 } n ? n : "Linehaul",
            qty, card.BaseRate, baseAmount, basis, card.Id));

        if (card.FuelSurchargePercent is { } pct && pct > 0)
        {
            var fuel = Round2(baseAmount * (pct / 100m));
            lines.Add(new ComputedCharge("fuel_surcharge", "FUEL", "Fuel surcharge",
                1m, Round4(pct / 100m), fuel, "pct_of_base", card.Id));
        }

        var total = lines.Sum(l => l.Amount);
        if (mode == RateMode.Preview)
            return new RateJobOutcome(true, "Rating preview", true, jobId, lines, total, card.Currency, null);

        return await CommitAsync(companyId, jobId, card, lines, total, ct);
    }

    private async Task<RateJobOutcome> CommitAsync(
        long companyId, long jobId, RateCardRecord card, List<ComputedCharge> lines, decimal total, CancellationToken ct)
    {
        // Never re-rate a job whose charges are already on an issued/locked invoice.
        var billed = await db.ScalarLongAsync(
            @"SELECT COUNT(*) FROM invoice_drafts
              WHERE company_id=@cid AND job_id=@jid AND status IN ('issued','pending_review','approved')",
            c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@jid", jobId); }, ct);
        if (billed > 0)
            return new RateJobOutcome(false, "Job already has an issued/in-review invoice — not re-rating",
                true, jobId, lines, total, card.Currency, "already_billed");

        await db.WithTransactionAsync(async (conn, tx) =>
        {
            // Delete only rating-owned charges not already on an issued invoice line. Manual charges untouched.
            await using (var del = new Npgsql.NpgsqlCommand(
                @"DELETE FROM job_charges jc
                  WHERE jc.company_id=@cid AND jc.job_id=@jid AND jc.source='rating'
                    AND NOT EXISTS (SELECT 1 FROM issued_invoice_lines il
                                    WHERE il.company_id=jc.company_id AND il.job_charge_id=jc.id)", conn, tx))
            {
                del.Parameters.AddWithValue("@cid", companyId);
                del.Parameters.AddWithValue("@jid", jobId);
                await del.ExecuteNonQueryAsync(ct);
            }
            foreach (var l in lines)
            {
                await using var ins = new Npgsql.NpgsqlCommand(
                    @"INSERT INTO job_charges
                        (company_id, job_id, rate_card_id, charge_code, charge_name, charge_type, description,
                         quantity, unit_rate, amount, currency, status, source, rate_basis, rated_at, created_at)
                      VALUES (@cid, @jid, @rc, @code, @name, @type, @desc,
                              @qty, @rate, @amt, @cur, 'pending', 'rating', @basis, NOW(), NOW())", conn, tx);
                ins.Parameters.AddWithValue("@cid", companyId);
                ins.Parameters.AddWithValue("@jid", jobId);
                ins.Parameters.AddWithValue("@rc", l.RateCardId);
                ins.Parameters.AddWithValue("@code", l.ChargeCode);
                ins.Parameters.AddWithValue("@name", l.ChargeName);
                ins.Parameters.AddWithValue("@type", l.ChargeType);
                ins.Parameters.AddWithValue("@desc", $"Rated {l.RateBasis} @ {l.UnitRate}");
                ins.Parameters.AddWithValue("@qty", l.Quantity);
                ins.Parameters.AddWithValue("@rate", l.UnitRate);
                ins.Parameters.AddWithValue("@amt", l.Amount);
                ins.Parameters.AddWithValue("@cur", card.Currency);
                ins.Parameters.AddWithValue("@basis", l.RateBasis);
                await ins.ExecuteNonQueryAsync(ct);
            }
            return true;
        }, ct);

        return new RateJobOutcome(true, $"Rated {lines.Count} charge(s)", true, jobId, lines, total, card.Currency, null);
    }

    private async Task<RateCardRecord?> ResolveRateCardAsync(long companyId, Dictionary<string, object?> job, CancellationToken ct)
    {
        if (job.GetValueOrDefault("rateCardId") is { } rc && rc is not DBNull)
        {
            var byId = await spine.GetRateCardByIdAsync(companyId, Convert.ToInt64(rc), ct);
            if (byId is not null) return byId;
        }
        if (job.GetValueOrDefault("contractId") is { } ck && ck is not DBNull)
        {
            var vt = job.GetValueOrDefault("requiredVehicleType")?.ToString();
            var row = await db.QuerySingleAsync(
                @"SELECT id FROM rate_cards
                  WHERE company_id=@cid AND contract_id=@k AND status IN ('Active','active')
                    AND effective_date <= CURRENT_DATE AND (expiry_date IS NULL OR expiry_date >= CURRENT_DATE)
                    AND (vehicle_type IS NULL OR vehicle_type = @vt)
                  ORDER BY (vehicle_type IS NOT NULL) DESC, effective_date DESC LIMIT 1",
                c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@k", Convert.ToInt64(ck));
                       c.Parameters.AddWithValue("@vt", (object?)vt ?? DBNull.Value); }, ct);
            if (row?.GetValueOrDefault("id") is { } id and not DBNull)
                return await spine.GetRateCardByIdAsync(companyId, Convert.ToInt64(id), ct);
        }
        return null;
    }

    private async Task<decimal?> ResolveMilesAsync(long companyId, long jobId, Dictionary<string, object?> job, CancellationToken ct)
    {
        var trip = await db.ScalarDecimalAsync(
            @"SELECT COALESCE(MAX(actual_distance_miles), MAX(planned_distance_miles))
              FROM trips WHERE company_id=@cid AND job_id=@jid",
            c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@jid", jobId); }, ct);
        if (trip is > 0) return trip;

        // Straight-line fallback from job coordinates.
        if (D(job, "pickupLatitude") is { } plat && D(job, "pickupLongitude") is { } plng &&
            D(job, "dropoffLatitude") is { } dlat && D(job, "dropoffLongitude") is { } dlng)
            return Round2((decimal)Haversine((double)plat, (double)plng, (double)dlat, (double)dlng));
        return null;
    }

    private async Task<int?> ResolveStopsAsync(long companyId, long jobId, CancellationToken ct)
    {
        var n = await db.ScalarLongAsync(
            @"SELECT COALESCE(MAX(total_planned_stops), 0) FROM trips WHERE company_id=@cid AND job_id=@jid",
            c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@jid", jobId); }, ct);
        return n > 0 ? (int)n : null;
    }

    private async Task<decimal?> ResolveHoursAsync(long companyId, long jobId, CancellationToken ct)
    {
        var mins = await db.ScalarDecimalAsync(
            @"SELECT MAX(actual_duration_minutes) FROM trips WHERE company_id=@cid AND job_id=@jid",
            c => { c.Parameters.AddWithValue("@cid", companyId); c.Parameters.AddWithValue("@jid", jobId); }, ct);
        return mins is > 0 ? Math.Round(mins.Value / 60m, 3, MidpointRounding.AwayFromZero) : null;
    }

    private static decimal? D(Dictionary<string, object?> row, string key)
        => row.GetValueOrDefault(key) is { } v && v is not DBNull ? Convert.ToDecimal(v) : null;

    // Canonical billing_basis; null = unsupported this phase.
    private static string? CanonicalBasis(string? basis) => (basis ?? "").Trim().ToLowerInvariant() switch
    {
        "flat rate" or "flat" or "per load" or "per_load" or "per unit" or "per_unit" => "flat",
        "per mile" or "per_mile" => "per_mile",
        "per kilometer" or "per_km" or "per km" => "per_km",
        "per stop" or "per_stop" => "per_stop",
        "hourly" or "per hour" or "per_hour" => "per_hour",
        _ => null,
    };

    private static double Haversine(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 3958.7613; // miles
        double dLat = (lat2 - lat1) * Math.PI / 180, dLon = (lon2 - lon1) * Math.PI / 180;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static decimal Round2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
    private static decimal Round4(decimal v) => Math.Round(v, 4, MidpointRounding.AwayFromZero);
    private static RateJobOutcome Unpriced(long jobId, string reason, string msg)
        => new(true, msg, false, jobId, Array.Empty<ComputedCharge>(), 0m, "USD", reason);
}
