using System.Security.Cryptography;
using System.Text;
using Opstrax.Api.Data;

namespace Opstrax.Api.Services;

// ─────────────────────────────────────────────────────────────────────────────
// FeatureFlagService — a REAL flag system (the previous screen was decorative:
// its toggle wrote a row and changed no behaviour).
//
// This one is actually consumed:
//   • Program.cs route kill-switch (e.g. /api/ai/* behind `ai_copilot`)
//   • any backend code path:  if (await flags.IsEnabledAsync(cid, "x", uid)) { ... }
//   • the UI, via GET /api/feature-flags/evaluate + useFlag()
//
// SEMANTICS (deliberate):
//   • enabled=false  → hard OFF for everyone. The kill switch always wins over rollout.
//   • rollout_pct    → DETERMINISTIC bucketing on (flagKey, subjectId). The same user
//     always lands in the same bucket for a given flag, so "20%" is a stable 20% of
//     users. A random draw per request would make the UI flicker and make incidents
//     impossible to reason about.
//   • unknown flag   → caller-supplied default. New features default OFF (fail-safe);
//     kill switches over EXISTING behaviour pass defaultOn:true so a tenant that has no
//     row yet is not silently broken.
// ─────────────────────────────────────────────────────────────────────────────
public sealed class FeatureFlagService(Database db)
{
    public sealed record DefaultFlag(string Key, string Name, string Description, bool Enabled, int RolloutPct);

    /// <summary>
    /// The flags every tenant gets. Seeded ENABLED so provisioning a tenant never changes
    /// behaviour — these are kill switches / ramp controls over features that already ship,
    /// not hidden features. Add an entry here plus a db/init migration to backfill existing
    /// tenants; new tenants pick it up at creation.
    /// </summary>
    public static readonly DefaultFlag[] Defaults =
    [
        new("ai_copilot", "AI Copilot & Recommendations",
            "Kill switch for all AI features (/api/ai/*). Turn off to immediately stop AI calls tenant-wide.",
            true, 100),
        new("pod_media_capture", "POD photo & signature capture",
            "Driver photo/signature upload for proof of delivery. Turn off (or dial back the rollout) and drivers fall back to the text evidence reference — delivery confirmation keeps working.",
            true, 100),
    ];

    /// <summary>Give a tenant the standard flag set. Idempotent.</summary>
    public async Task SeedDefaultsAsync(long companyId, CancellationToken ct = default)
    {
        foreach (var f in Defaults)
        {
            await db.ExecuteAsync(
                @"INSERT INTO feature_flags (company_id, flag_key, name, description, enabled, rollout_pct, environment)
                  VALUES (@c, @k, @n, @d, @e, @p, 'production')
                  ON CONFLICT (company_id, flag_key) DO NOTHING",
                c =>
                {
                    c.Parameters.AddWithValue("@c", companyId);
                    c.Parameters.AddWithValue("@k", f.Key);
                    c.Parameters.AddWithValue("@n", f.Name);
                    c.Parameters.AddWithValue("@d", f.Description);
                    c.Parameters.AddWithValue("@e", f.Enabled);
                    c.Parameters.AddWithValue("@p", f.RolloutPct);
                }, ct);
        }
    }

    /// <summary>Stable bucket for (flag, subject). Same inputs → same answer, always.</summary>
    public static bool InRollout(string flagKey, long subjectId, int rolloutPct)
    {
        if (rolloutPct >= 100) return true;
        if (rolloutPct <= 0) return false;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{flagKey}:{subjectId}"));
        var bucket = BitConverter.ToUInt32(hash, 0) % 100u; // 0..99
        return bucket < (uint)rolloutPct;
    }

    /// <summary>Resolve one flag for one subject. Kill switch beats rollout.</summary>
    public async Task<bool> IsEnabledAsync(
        long companyId, string flagKey, long subjectId, bool defaultOn = false, CancellationToken ct = default)
    {
        var row = await db.QuerySingleAsync(
            "SELECT enabled, rollout_pct FROM feature_flags WHERE company_id=@c AND flag_key=@k",
            c => { c.Parameters.AddWithValue("@c", companyId); c.Parameters.AddWithValue("@k", flagKey); }, ct);

        return Resolve(row, flagKey, subjectId, defaultOn);
    }

    /// <summary>Shared resolution so the middleware and the service can never disagree.</summary>
    public static bool Resolve(Dictionary<string, object?>? row, string flagKey, long subjectId, bool defaultOn)
    {
        if (row is null) return defaultOn;                       // no row → caller's default
        if (row["enabled"] is not bool enabled || !enabled) return false; // kill switch wins
        var pct = row["rolloutPct"] is { } p && p is not DBNull ? Convert.ToInt32(p) : 100;
        return InRollout(flagKey, subjectId, pct);
    }

    /// <summary>Every flag resolved for this subject — what the UI consumes.</summary>
    public async Task<Dictionary<string, bool>> EvaluateAllAsync(
        long companyId, long subjectId, CancellationToken ct = default)
    {
        var rows = await db.QueryAsync(
            "SELECT flag_key, enabled, rollout_pct FROM feature_flags WHERE company_id=@c ORDER BY flag_key",
            c => c.Parameters.AddWithValue("@c", companyId), ct);

        var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows)
        {
            var key = r["flagKey"]?.ToString();
            if (string.IsNullOrWhiteSpace(key)) continue;
            map[key!] = Resolve(r, key!, subjectId, defaultOn: false);
        }
        return map;
    }
}
