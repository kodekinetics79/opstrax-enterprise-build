export function resolveAuthorizedSummaryCount(
  summarySucceeded: boolean,
  value: unknown,
): number | null {
  if (!summarySucceeded || value == null || value === "") return null;
  if (typeof value !== "number" && typeof value !== "string") return null;
  if (typeof value === "string" && (value !== value.trim() || !/^\d+$/.test(value))) return null;
  const parsed = typeof value === "number" ? value : Number(value);
  return Number.isSafeInteger(parsed) && parsed >= 0 ? parsed : null;
}
