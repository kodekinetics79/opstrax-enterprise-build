// ── Telemetry provenance + freshness ─────────────────────────────────────────
// The live map must show an operator not just WHERE a vehicle is but WHERE THE FIX
// CAME FROM and HOW OLD IT IS. These helpers are the single source of truth for how
// provenance (source vocabulary) and freshness (age of the fix) map to human labels
// and visual treatments, shared by the map markers, roster rows, popups, detail
// drawer and legend so they never drift.
//
// EVERYTHING here is tolerant of missing data: an older API or a pre-migration DB
// (production runs as a restricted role that skips schema init, so the provenance
// columns may not exist yet) simply yields source === null, which classifies as
// "unknown" and renders a neutral treatment. Nothing here throws on absent fields.

/** Coarse provenance category driving the visual treatment. */
export type ProvenanceCategory = "direct" | "simulated" | "seeded" | "vendor" | "unknown";

/** Freshness bucket by age of the fix (seconds since received_at). */
export type FreshnessBucket = "live" | "delayed" | "stale" | "offline";

/**
 * Raw source vocabulary → coarse category.
 *   native_eld / gateway → direct-device fix (trustworthy real hardware)
 *   simulator            → simulated (MUST look distinct from a real fix)
 *   legacy / seed        → seeded / backfill (MUST look distinct from a real fix)
 *   partner_api          → vendor pull (Samsara etc.)
 *   anything else / null → unknown
 */
export function classifySource(source: string | null | undefined): ProvenanceCategory {
  const s = String(source ?? "").trim().toLowerCase();
  switch (s) {
    case "native_eld":
    case "gateway":
      return "direct";
    case "simulator":
      return "simulated";
    case "legacy":
    case "seed":
      return "seeded";
    case "partner_api":
      return "vendor";
    default:
      return "unknown";
  }
}

/** Human-readable label for a raw source token (falls back to "Unknown source"). */
export function sourceLabel(source: string | null | undefined): string {
  const s = String(source ?? "").trim().toLowerCase();
  switch (s) {
    case "native_eld":  return "Native ELD";
    case "gateway":     return "Gateway (GT06)";
    case "simulator":   return "Simulator";
    case "partner_api": return "Partner API";
    case "legacy":      return "Legacy backfill";
    case "seed":        return "Seed data";
    default:            return "Unknown source";
  }
}

/** Short badge text per category — surfaced on markers, roster rows and the legend. */
export function categoryBadge(category: ProvenanceCategory): string {
  switch (category) {
    case "direct":    return "DIRECT";
    case "simulated": return "SIM";
    case "seeded":    return "SEED";
    case "vendor":    return "VENDOR";
    case "unknown":   return "?";
  }
}

/** Accent color per category — used for badges and marker source-rings. */
export function categoryColor(category: ProvenanceCategory): string {
  switch (category) {
    case "direct":    return "#0d9488"; // teal-600
    case "simulated": return "#8b5cf6"; // violet-500
    case "seeded":    return "#64748b"; // slate-500
    case "vendor":    return "#2563eb"; // blue-600
    case "unknown":   return "#94a3b8"; // slate-400
  }
}

/**
 * CSS outline fragment that rings a marker to signal its source. Direct fixes get
 * NO ring (they are the trusted baseline); every synthetic/foreign source gets a
 * distinct ring so a simulated or seeded dot can never be mistaken for a real one.
 * Outline (not border) is used so it never perturbs Leaflet's icon layout.
 */
export function markerSourceRing(category: ProvenanceCategory): string {
  switch (category) {
    case "simulated": return "outline:2px dashed #8b5cf6;outline-offset:2px;";
    case "seeded":    return "outline:2px dotted #64748b;outline-offset:2px;";
    case "vendor":    return "outline:2px solid #2563eb;outline-offset:2px;";
    case "unknown":   return "outline:2px dashed #94a3b8;outline-offset:2px;";
    case "direct":
    default:          return "";
  }
}

/**
 * Freshness bucket from seconds-since-fix.
 *   null/NaN → offline (no usable fix time)
 *   <=120s   → live
 *   <=900s   → delayed
 *   >900s    → stale  (matches the existing server-side is_stale > 900 threshold)
 */
export function freshnessBucket(seconds: number | null | undefined): FreshnessBucket {
  if (seconds == null || !Number.isFinite(seconds)) return "offline";
  const s = Math.max(0, seconds);
  if (s <= 120) return "live";
  if (s <= 900) return "delayed";
  return "stale";
}

/**
 * Parse a SERVER-supplied freshness string ('live' | 'delayed' | 'stale' | 'offline')
 * into a bucket. The server computes freshness from the age of the actual GPS fix
 * (the worst of receipt-age and device-fix-age), so it is the AUTHORITATIVE currency
 * signal and must be preferred over a client-side bucket derived from secondsSincePing
 * (which only sees pipeline-receipt age and would mislabel a backdated/buffered fix as
 * live). Returns null for an empty/unrecognized value so callers can fall back.
 */
export function bucketFromServerFreshness(freshness: string | null | undefined): FreshnessBucket | null {
  switch (String(freshness ?? "").trim().toLowerCase()) {
    case "live":    return "live";
    case "delayed": return "delayed";
    case "stale":   return "stale";
    case "offline": return "offline";
    default:        return null;
  }
}

/** Marker/dot color per freshness bucket — stale/offline are deliberately muted. */
export function freshnessColor(bucket: FreshnessBucket): string {
  switch (bucket) {
    case "live":    return "#14b8a6"; // teal-500
    case "delayed": return "#f59e0b"; // amber-500
    case "stale":   return "#94a3b8"; // slate-400
    case "offline": return "#64748b"; // slate-500
  }
}

/** Title-case label for a freshness bucket. */
export function freshnessBucketLabel(bucket: FreshnessBucket): string {
  switch (bucket) {
    case "live":    return "Live";
    case "delayed": return "Delayed";
    case "stale":   return "Stale";
    case "offline": return "Offline";
  }
}

/**
 * Read the raw source off a loosely-typed entity/record, tolerating both the
 * camelCase (API-mapped) and snake_case (raw DB column) shapes, returning null
 * when the field is absent entirely.
 */
export function readSource(rec: Record<string, unknown> | null | undefined): string | null {
  if (!rec) return null;
  const raw = rec["source"] ?? rec["Source"];
  const s = raw == null ? "" : String(raw).trim();
  return s === "" ? null : s;
}
