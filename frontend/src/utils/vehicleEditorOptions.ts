export const VEHICLE_TYPE_OPTIONS = [
  "Truck",
  "Tractor",
  "Van",
  "Box Truck",
  "Reefer",
  "Tanker",
] as const;

export function optionsWithPersistedValue(
  options: readonly string[],
  persistedValue: unknown,
): readonly string[] {
  const value = String(persistedValue ?? "").trim();
  if (value === "" || options.includes(value)) return options;
  return [value, ...options];
}
