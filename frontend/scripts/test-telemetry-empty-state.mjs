import assert from "node:assert/strict";
import { resolveTelemetryEmptyState } from "../src/utils/telemetryEmptyState.ts";

const decide = (overrides = {}) => resolveTelemetryEmptyState({
  rowCount: 0,
  searchInput: "",
  appliedSearch: "",
  tab: "All",
  ...overrides,
});

assert.equal(decide(), "tenant-empty", "only an unfiltered empty inventory is tenant-empty");
assert.equal(decide({ searchInput: "missing-device" }), "filtered-empty", "typed no-match is filtered-empty");
assert.equal(decide({ appliedSearch: "missing-device" }), "filtered-empty", "debounced in-flight no-match remains filtered-empty");
assert.equal(decide({ tab: "Offline" }), "filtered-empty", "an empty non-All tab is filtered-empty");
assert.equal(decide({ rowCount: 1, searchInput: "missing-device", tab: "Offline" }), "rows", "rendered rows always win");

console.log("Telemetry empty-state behavior contract passed.");
