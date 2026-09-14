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
