import assert from "node:assert/strict";
import { connectorAttemptHealth } from "../src/lib/connectorFreshness.ts";

const start = Date.parse("2026-09-02T12:00:00Z");
const base = {
  key: "samsara",
  status: "Connected",
  syncLastAttemptAt: "2026-09-02T12:00:00Z",
  syncLastCompletedAt: "2026-09-02T12:00:20Z",
  syncLastOk: true,
  providerLastEventAt: "2026-09-02T12:00:10Z",
};

assert.equal(connectorAttemptHealth({ ...base, syncLastAttemptAt: null, syncLastCompletedAt: null }, start)?.state, "awaiting");
assert.equal(connectorAttemptHealth({ ...base, syncLastCompletedAt: null }, start + 30_000)?.state, "in-progress");
assert.equal(connectorAttemptHealth({ ...base, syncLastCompletedAt: null }, start + 91_000)?.state, "error");
assert.equal(connectorAttemptHealth(base, start + 14 * 60_000)?.state, "fresh");
assert.equal(connectorAttemptHealth(base, start + 16 * 60_000)?.state, "stale");
assert.equal(connectorAttemptHealth(base, start + 17 * 60_000)?.state, "stale", "stale state remains stable after its single threshold transition");
assert.equal(
  connectorAttemptHealth(base, start + 16 * 60_000)?.announcement,
  connectorAttemptHealth(base, start + 17 * 60_000)?.announcement,
  "accessible announcement stays stable while only visual relative time changes",
);
assert.equal(connectorAttemptHealth({ ...base, syncLastOk: false, status: "Error" }, start + 30_000)?.state, "error");
assert.equal(
  connectorAttemptHealth({ ...base, status: "Error", syncLastOk: true }, start + 30_000)?.state,
  "fresh",
  "a later failed handshake cannot rewrite a successful sync result",
);
assert.equal(
  connectorAttemptHealth({ ...base, providerLastEventAt: null }, start + 30_000)?.state,
  "awaiting",
  "a successful poll without provider telemetry is not green",
);
const providerStale = connectorAttemptHealth(
  {
    ...base,
    syncLastAttemptAt: "2026-09-02T12:16:00Z",
    syncLastCompletedAt: "2026-09-02T12:16:10Z",
    providerLastEventAt: "2026-09-02T12:00:00Z",
  },
  start + 17 * 60_000,
);
assert.equal(providerStale?.state, "stale");
assert.match(providerStale?.label ?? "", /Provider telemetry stale/, "provider-event staleness is independent of a fresh polling attempt");
assert.equal(connectorAttemptHealth({ ...base, key: "twilio" }, start + 30_000), null, "non-polling connectors do not inherit Samsara freshness semantics");

console.log("Connector freshness behavior passed.");
