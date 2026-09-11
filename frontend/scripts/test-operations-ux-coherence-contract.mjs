import assert from "node:assert/strict";
import fs from "node:fs";

const pages = {
  fleetHealth: fs.readFileSync(new URL("../src/pages/FleetHealthPage.tsx", import.meta.url), "utf8"),
  controlTower: fs.readFileSync(new URL("../src/pages/ControlTowerPage.tsx", import.meta.url), "utf8"),
  liveMap: fs.readFileSync(new URL("../src/pages/LiveMapPage.tsx", import.meta.url), "utf8"),
  alerts: fs.readFileSync(new URL("../src/pages/AlertsCenterPage.tsx", import.meta.url), "utf8"),
  geofences: fs.readFileSync(new URL("../src/pages/GeofenceManagementPage.tsx", import.meta.url), "utf8"),
};

for (const [name, source] of Object.entries(pages)) {
  assert.match(source, /control-tower/, `${name} must use the shared Operations workspace shell`);
  assert.match(source, /<PageHeader/, `${name} must use the shared Operations page header`);
}

assert.doesNotMatch(pages.liveMap, /live-map-workbench|live-map-stage|live-map-tactile-card/, "Fleet Position Map must not restore its one-off warm theme");
assert.doesNotMatch(pages.alerts, /alerts-command-room|alerts-center-workbench/, "Alerts Center must not restore its oversized one-off command-room theme");

assert.match(pages.fleetHealth, /Entity/, "Fleet Health filters must visibly label their entity group");
assert.match(pages.fleetHealth, /Severity/, "Fleet Health filters must visibly label their severity group");
assert.match(pages.fleetHealth, /aria-pressed=/, "Fleet Health filters must expose their selected state");
assert.match(pages.fleetHealth, /Coverage unavailable/, "Fleet Health must distinguish missing coverage from a measured zero");
assert.doesNotMatch(pages.fleetHealth, /qualified vehicles/, "Fleet Health must not call no-known-block vehicles qualified when readiness coverage is incomplete");

assert.match(pages.controlTower, /hasCurrentTelemetryEvidence/, "Control Tower must gate telemetry quality on current device and position evidence");
assert.match(pages.controlTower, /Recorded Event Feed/, "Control Tower must label persisted events as recorded evidence");
assert.match(pages.controlTower, /Recorded \{timestamp\}/, "Control Tower event timestamps must disclose that they are recorded times");

assert.match(pages.alerts, /agingUnresolved/, "Alerts Center must compute aging from unresolved records");
assert.match(pages.alerts, /criticalUnresolved/, "Alerts Center severity counts must use the unresolved queue");
assert.match(pages.alerts, /Aging unresolved/, "Alerts Center must label the age metric consistently with its denominator");
assert.match(pages.alerts, /useDialogFocus/, "Alerts action dialogs must trap and restore focus and close on Escape");
assert.match(pages.alerts, /aria-labelledby="alert-action-title"/, "Alerts action dialogs must expose an accessible name");
assert.match(pages.alerts, /aria-label="Alert categories"[\s\S]*aria-label="Alert severity"[\s\S]*aria-label="Alert status"/, "Alerts filters must expose labeled groups");
assert.match(pages.alerts, /aria-pressed=/, "Alerts filters must expose their selected state");

assert.match(pages.geofences, /aria-label="Geofence status"/, "Geofence status filters must be grouped and labeled");
assert.match(pages.geofences, /aria-pressed=/, "Geofence status filters must expose their selected state");
assert.doesNotMatch(pages.geofences, /[✎✕⬡]/, "Geofence actions must use the shared icon system instead of raw glyphs");
assert.match(pages.geofences, /summaryQ\.isSuccess && s\?\.entryEventsToday != null/, "Geofence activity metrics must remain unavailable until the summary request succeeds");
assert.doesNotMatch(pages.geofences, /(?:entryEventsToday|exitEventsToday|vehiclesTriggered)\s*\?\?\s*0/, "Missing geofence activity evidence must not become a measured zero");
assert.match(pages.geofences, /eventsQ\.isLoading[\s\S]*eventsQ\.isError[\s\S]*events\.length === 0/, "Geofence events must distinguish loading, failure, and an authoritative empty result");
assert.match(pages.geofences, /useDialogFocus/, "Geofence dialogs must trap and restore focus and close on Escape");
assert.match(pages.geofences, /aria-labelledby="geofence-editor-title"/, "The geofence editor must expose an accessible name");
assert.match(pages.geofences, /Pan with arrow keys and press Enter or Space/, "The geofence map creation flow must expose a keyboard path");
assert.match(pages.geofences, /aria-pressed=\{Boolean\(isSel\)\}/, "Zone selection must use a keyboard-operable control with selected state");

console.log("Operations UX coherence contract passed.");
