/** Treat future, missing and invalid measurement times as unverified. */
export function observationAge(value: unknown, now = Date.now()): number | null {
  if (value == null || value === "") return null;
  const time = Date.parse(String(value));
  return Number.isFinite(time) && time <= now ? now - time : null;
}
export function recentObservation(value: unknown, now = Date.now(), windowMs = 180_000): boolean {
  const age = observationAge(value, now);
  return age != null && age <= windowMs;
}
export function observedMotion(speed: number | null, time: unknown, now = Date.now()): string {
  if (speed == null || !recentObservation(time, now)) return "Current movement unknown";
  return speed > 1 ? "Moving" : "Stationary";
}
export function measuredNumber(value: unknown, unit = ""): string {
  if (value == null || value === "" || !Number.isFinite(Number(value))) return "Unknown";
  return `${Number(value).toLocaleString()}${unit ? ` ${unit}` : ""}`;
}
export type DeviceWorkspaceSection = "configuration" | "commands" | "diagnostics";
export function deviceWorkspaceRoute(id: string | number, section?: DeviceWorkspaceSection): string {
  return `/iot-devices?deviceId=${encodeURIComponent(String(id))}${section ? `&deviceSection=${section}` : ""}`;
}
