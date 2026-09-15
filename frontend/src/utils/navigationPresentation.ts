type NavigationModule = { key: string; route: string; title: string; description?: string; group?: string };

// Only destinations verified to render the same page and mode are consolidated.
// Call after access filtering so an alias can never grant access to another route.
export const NAVIGATION_ALIASES: Record<string, string> = {
  "load-bookings": "jobs",
  "sales-pipeline": "opportunities",
};

const LABELS: Record<string, string> = {
  jobs: "Jobs & Bookings",
  opportunities: "Pipeline & Opportunities",
  "cold-chain": "Temperature Readings",
  "fleet-cold-chain": "Cold Chain Operations",
  "gps-tracking": "GPS Diagnostics",
  "telematics-control-tower": "Device Operations",
};

export function consolidateNavigation<T extends NavigationModule>(items: T[]) {
  const grouped = new Map<string, T[]>();
  for (const item of items) {
    const key = NAVIGATION_ALIASES[item.key] ?? item.key;
    grouped.set(key, [...(grouped.get(key) ?? []), item]);
  }
  return [...grouped.entries()].map(([key, members]) => {
    const target = members.find(item => item.key === key) ?? members[0];
    return {
      ...target,
      title: LABELS[key] ?? target.title,
      navigationRoutes: members.map(item => item.route),
      navigationSearch: members.map(item => [item.key, item.title, item.description, item.route, item.group].join(" ")).join(" "),
    };
  });
}

export function navigationRouteActive(item: { route: string; navigationRoutes?: string[] }, pathname: string) {
  const path = pathname.replace(/\/+$/, "") || "/";
  return (item.navigationRoutes ?? [item.route]).some(route => {
    const normalized = route.replace(/\/+$/, "") || "/";
    return path === normalized || path.startsWith(`${normalized}/`);
  });
}

export function splitNavigationItems<T extends { route: string; navigationRoutes?: string[] }>(items: T[], pathname: string, limit = 6) {
  const primary = items.filter((item, index) => index < limit || navigationRouteActive(item, pathname));
  return { primary, more: items.filter(item => !primary.includes(item)) };
}
