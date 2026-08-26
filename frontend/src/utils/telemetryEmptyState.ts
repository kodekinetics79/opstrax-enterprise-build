export type TelemetryEmptyState = "rows" | "filtered-empty" | "tenant-empty";

export function resolveTelemetryEmptyState(input: {
  rowCount: number;
  searchInput: string;
  appliedSearch: string;
  tab: string;
}): TelemetryEmptyState {
  if (input.rowCount > 0) return "rows";
  const hasActiveFilter = input.searchInput.trim().length > 0
    || input.appliedSearch.trim().length > 0
    || input.tab !== "All";
  return hasActiveFilter ? "filtered-empty" : "tenant-empty";
}
