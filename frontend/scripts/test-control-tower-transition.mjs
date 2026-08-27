import assert from "node:assert/strict";
import fs from "node:fs";

const page = fs.readFileSync(new URL("../src/pages/TelematicsControlTowerPage.tsx", import.meta.url), "utf8");

assert.match(
  page,
  /const queueTransitionPending = searchPending \|\| \(query\.isLoading && hasLoadedQueue\.current\)/,
  "the transition remains pending after debounce while the new server query loads",
);
assert.match(
  page,
  /if \(query\.isLoading && !hasLoadedQueue\.current\) return <LoadingState \/>/,
  "only the initial load replaces the whole page",
);
assert.match(
  page,
  /const lastSuccessfulSummary = useRef<DevicePageResult\["summary"\] \| null>\(null\)/,
  "the full-fleet KPI summary has a last-successful snapshot",
);
assert.match(
  page,
  /lastSuccessfulSummary\.current = query\.data\.summary/,
  "only successful query data updates the KPI snapshot",
);
assert.match(
  page,
  /const summary = query\.data\?\.summary \?\? lastSuccessfulSummary\.current/,
  "query-key transitions retain truthful full-fleet KPIs instead of flashing zeroes",
);
assert.match(page, /aria-busy=\{queueTransitionPending\}/, "the priority queue exposes its busy state");
assert.match(
  page,
  /queueTransitionPending \? \([\s\S]*role="status" aria-live="polite" aria-busy="true"[\s\S]*Updating priority queue…/,
  "the pending queue has a visible polite status announcement",
);
assert.match(
  page,
  /!queueTransitionPending \? <div[\s\S]*Page \{page\} of \{pageCount\}/,
  "stale range and paging controls remain hidden for the complete transition",
);

console.log("Telematics Control Tower transition contract passed.");
