/** Unknown measurements remain null; zero is a real measured value. */
export function optionalTelemetryNumber(value: unknown): number | null {
  if (typeof value !== "number" && typeof value !== "string") return null;
  if (typeof value === "string" && value.trim() === "") return null;
  const number = Number(value);
  return Number.isFinite(number) ? number : null;
}

export function optionalTelemetrySpeed(value: unknown): number | null {
  const speed = optionalTelemetryNumber(value);
  return speed != null && speed >= 0 ? speed : null;
}

export function optionalTelemetryHeading(value: unknown): number | null {
  const heading = optionalTelemetryNumber(value);
  return heading != null && heading >= 0 && heading <= 360 ? heading % 360 : null;
}

export function readSpeedMph(row: { speedMph?: unknown; speed_mph?: unknown }): number | null {
  // Explicit canonical null must not revive an older snake-case alias value.
  return optionalTelemetrySpeed(row.speedMph !== undefined ? row.speedMph : row.speed_mph);
}

export function telemetryMotion(speed: unknown, threshold = 3): "Moving" | "Idle" | "Unknown" {
  const measured = optionalTelemetrySpeed(speed);
  return measured == null ? "Unknown" : measured > threshold ? "Moving" : "Idle";
}

export function formatTelemetrySpeed(value: unknown, unit = "mph"): string {
  const speed = optionalTelemetrySpeed(value);
  return speed == null ? "Speed unavailable" : `${Math.round(speed)} ${unit}`;
}

export function telemetrySpeedSummary(values: readonly unknown[]): { peak: number | null; knownCount: number; missingCount: number } {
  let peak: number | null = null;
  let knownCount = 0;
  for (const value of values) {
    const speed = optionalTelemetrySpeed(value);
    if (speed == null) continue;
    knownCount++;
    peak = peak == null ? speed : Math.max(peak, speed);
  }
  return { peak, knownCount, missingCount: values.length - knownCount };
}
