/** Preserve exact device identity; the detail API enforces tenant and permission scope. */
export function deviceDetailsRoute(id: unknown): string {
  return id == null || !String(id).trim() ? "/iot-devices" : `/iot-devices?deviceId=${encodeURIComponent(String(id))}`;
}
export function displayRecordValue(value: unknown): string {
  if (value == null || value === "") return "—";
  const text = String(value);
  if (/^\d{4}-\d{2}-\d{2}T/.test(text)) {
    const date = new Date(text);
    if (!Number.isNaN(date.getTime())) return date.toLocaleDateString(undefined, { year: "numeric", month: "short", day: "numeric", timeZone: "UTC" });
  }
  return text;
}

/** Device endpoints accept positive signed 64-bit inventory IDs only. */
export function linkedDeviceId(value: string | null): string | null {
  if (!value || !/^[1-9]\d{0,18}$/.test(value) || BigInt(value) > 9223372036854775807n) return null;
  return value;
}

/** Only a not-found active vehicle permits an explicit archived lookup. */
export async function readLinkedVehicle<T extends object>(id: string, read: (id: string, lifecycle?: "active" | "archived") => Promise<T>) {
  try { return { ...(await read(id)), archived: false }; }
  catch (error) {
    if ((error as { response?: { status?: number } }).response?.status !== 404) throw error;
    return { ...(await read(id, "archived")), archived: true };
  }
}
export function uniqueDetailScores(entries: [string, unknown][], record: Record<string, unknown>) {
  return entries.filter(([key, value]) => /score/i.test(key) && value != null && typeof value !== "object")
    .filter(([key]) => !(key === "driverReadinessScore" && record.readinessScore != null) && !(key === "riskHeatScore" && record.riskScore != null));
}
