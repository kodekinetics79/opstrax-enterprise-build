export type FleetQueryPresentation = "loading" | "settling" | "rows" | "empty";

export interface FleetQueryIdentity {
  page: number;
  pageSize: number;
  search: string;
  status: string;
  sort: string;
  order: string;
}

export function fleetQueryFingerprint(identity: FleetQueryIdentity): string {
  return JSON.stringify({
    page: Math.max(1, identity.page),
    pageSize: identity.pageSize,
    search: identity.search.trim(),
    status: identity.status,
    sort: identity.sort,
    order: identity.order,
  });
}

export function resolveFleetQueryPresentation(input: {
  rawSearch: string;
  appliedSearch: string;
  requestFingerprint: string;
  responseFingerprint?: string;
  hasData: boolean;
  isFetching: boolean;
}): FleetQueryPresentation {
  // The raw value changes on the first render after a keystroke, while the applied
  // server search intentionally waits for debounce. Never present rows from the old
  // search during that window, even if they are still valid React Query cache data.
  if (input.rawSearch.trim() !== input.appliedSearch.trim()) return "settling";

  // Defensive identity check for page/filter/sort transitions. React Query normally
  // clears data when its key changes, but rows are only renderable when the resolved
  // request explicitly identifies the current intent.
  if (input.hasData && input.responseFingerprint !== input.requestFingerprint) return "settling";
  if (!input.hasData && input.isFetching) return "loading";
  return input.hasData ? "rows" : "empty";
}
