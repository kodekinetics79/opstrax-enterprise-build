export interface VehicleMarkerAccessibleNameInput {
  label: string;
  fallbackId: string;
  driver: string;
  freshness: string;
  operationalStatus: string;
  speedMph?: number | null;
}

function clean(value: string): string {
  return value.replace(/\s+/g, " ").trim();
}

/**
 * Build the concise, data-backed name announced for a focusable fleet-map marker.
 * The fallback identifier is used only when the upstream label is missing/generic,
 * so assistive users never encounter a run of indistinguishable "Vehicle" buttons.
 */
export function buildVehicleMarkerAccessibleName({
  label,
  fallbackId,
  driver,
  freshness,
  operationalStatus,
  speedMph,
}: VehicleMarkerAccessibleNameInput): string {
  const normalizedLabel = clean(label);
  const normalizedFallback = clean(fallbackId);
  const identity = normalizedLabel && normalizedLabel.toLowerCase() !== "vehicle"
    ? normalizedLabel
    : normalizedFallback || "unknown";
  const normalizedDriver = clean(driver);
  const normalizedFreshness = clean(freshness).toLowerCase() || "unknown";
  const normalizedStatus = clean(operationalStatus).toLowerCase() || "unknown";
  const speed = speedMph != null && Number.isFinite(speedMph)
    ? `, ${Math.round(speedMph)} miles per hour`
    : "";

  return `Vehicle ${identity}, position ${normalizedFreshness}, status ${normalizedStatus}, driver ${normalizedDriver || "unassigned"}${speed}`;
}
