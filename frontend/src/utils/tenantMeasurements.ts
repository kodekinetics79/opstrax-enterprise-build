const KILOMETRES_PER_MILE = 1.609344;

export function usesMetricDistance(country: string | null | undefined): boolean {
  return country === "SA" || country === "CA";
}

export function tenantDistanceUnit(country: string | null | undefined): "km" | "mi" {
  return usesMetricDistance(country) ? "km" : "mi";
}

export function tenantSpeedUnit(country: string | null | undefined): "km/h" | "mph" {
  return usesMetricDistance(country) ? "km/h" : "mph";
}

export function milesToTenantDistance(value: unknown, country: string | null | undefined): number | null {
  if (value == null || value === "" || !Number.isFinite(Number(value))) return null;
  const miles = Number(value);
  return usesMetricDistance(country) ? miles * KILOMETRES_PER_MILE : miles;
}

export function tenantDistanceToMiles(value: unknown, country: string | null | undefined): number | null {
  if (value == null || value === "" || !Number.isFinite(Number(value))) return null;
  const distance = Number(value);
  return usesMetricDistance(country) ? distance / KILOMETRES_PER_MILE : distance;
}

export function mphToTenantSpeed(value: unknown, country: string | null | undefined): number | null {
  if (value == null || value === "" || !Number.isFinite(Number(value))) return null;
  const mph = Number(value);
  return usesMetricDistance(country) ? mph * KILOMETRES_PER_MILE : mph;
}

export function formatTenantDistanceFromMiles(value: unknown, country: string | null | undefined): string {
  const distance = milesToTenantDistance(value, country);
  return distance == null ? "Unknown" : `${Math.round(distance).toLocaleString()} ${tenantDistanceUnit(country)}`;
}
