import assert from "node:assert/strict";
import fs from "node:fs";
import { resolveAuthorizedSummaryCount } from "../src/utils/vehicleSummaryPresentation.ts";
import { summarizePositionFreshness } from "../src/utils/telemetryProvenance.ts";
import { summarizeControlTowerStatus } from "../src/utils/controlTowerStatus.ts";
import { buildVehicleMarkerAccessibleName } from "../src/utils/mapAccessibility.ts";
import { optionsWithPersistedValue, VEHICLE_TYPE_OPTIONS } from "../src/utils/vehicleEditorOptions.ts";

const audit = fs.readFileSync(new URL("../src/pages/AuditLogsPage.tsx", import.meta.url), "utf8");
const vehicles = fs.readFileSync(new URL("../src/pages/VehiclesPage.tsx", import.meta.url), "utf8");
const liveMap = fs.readFileSync(new URL("../src/pages/LiveMapPage.tsx", import.meta.url), "utf8");
const liveMapComponent = fs.readFileSync(new URL("../src/components/LiveMap.tsx", import.meta.url), "utf8");
const geofences = fs.readFileSync(new URL("../src/pages/GeofenceManagementPage.tsx", import.meta.url), "utf8");
const controlTower = fs.readFileSync(new URL("../src/pages/ControlTowerPage.tsx", import.meta.url), "utf8");
const commandCenter = fs.readFileSync(new URL("../src/pages/CommandCenterPage.tsx", import.meta.url), "utf8");
const alertsCenter = fs.readFileSync(new URL("../src/pages/AlertsCenterPage.tsx", import.meta.url), "utf8");
const aiCopilot = fs.readFileSync(new URL("../src/pages/AiCopilotPage.tsx", import.meta.url), "utf8");
const moduleConfig = fs.readFileSync(new URL("../src/modules/moduleConfig.ts", import.meta.url), "utf8");

assert.doesNotMatch(
  audit,
  /immutable|all system actions/i,
  "Audit UI must not claim unproven immutability or complete module coverage",
);
assert.match(
  audit,
  /Operational record of recorded system activity for internal review/,
  "Audit UI must use bounded operational-record language",
);
assert.match(
  audit,
  /do not constitute a legally certified audit trail/,
  "Audit UI must retain the legal-certification disclaimer",
);

assert.match(
  vehicles,
  /moving on page[\s\S]*available on page[\s\S]*need attention in authorized scope/,
  "Vehicle header must distinguish page-scoped and authorized-scope counts",
);
assert.match(
  vehicles,
  /label="Page readiness"[\s\S]*assessed on this page/,
  "Readiness KPI must disclose page scope",
);
assert.match(
  vehicles,
  /label="Moving on page"[\s\S]*available on this page/,
  "Movement KPI must disclose page scope",
);
assert.match(vehicles, /label="Authorized scope at risk"/, "At-risk KPI must disclose authorized scope");
assert.match(vehicles, /label="Authorized scope device \/ camera gaps"/, "Device-gap KPI must disclose authorized scope");
assert.match(vehicles, /\{moving\} moving on page/, "Vehicle footer movement count must disclose page scope");
assert.doesNotMatch(vehicles, /need attention fleet-wide|Fleet-wide high risk|Fleet-wide telematics/, "Restricted users must not see tenant-wide scope claims");

assert.match(geofences, /aria-label={`Edit geofence \${String\(zone\.name \?\? zone\.id\)}`}/, "Each geofence edit control must identify its target zone");
assert.match(geofences, /aria-label={`Delete geofence \${String\(zone\.name \?\? zone\.id\)}`}/, "Each geofence delete control must identify its target zone");
assert.match(geofences, /aria-label={`View events for geofence \${String\(zone\.name \?\? zone\.id\)}`}/, "Each geofence event control must identify its target zone");
assert.match(geofences, /aria-label="Close geofence dialog"/, "The geofence editor close control must expose its purpose");

assert.ok(VEHICLE_TYPE_OPTIONS.includes("Tractor"), "The governed large-fleet Tractor type must be editable");
assert.deepEqual(
  optionsWithPersistedValue(VEHICLE_TYPE_OPTIONS, "Tractor"),
  VEHICLE_TYPE_OPTIONS,
  "A governed vehicle type must remain selected without duplicating its option",
);
assert.deepEqual(
  optionsWithPersistedValue(VEHICLE_TYPE_OPTIONS, "Specialized Heavy Unit"),
  ["Specialized Heavy Unit", ...VEHICLE_TYPE_OPTIONS],
  "An existing imported type must remain editable without silently coercing the persisted value",
);

assert.equal(resolveAuthorizedSummaryCount(true, 0), 0, "A legitimate summary zero must remain zero");
assert.equal(resolveAuthorizedSummaryCount(true, "0"), 0, "A serialized summary zero must remain zero");
assert.equal(resolveAuthorizedSummaryCount(true, 51), 51, "A valid summary count must be preserved");
assert.equal(resolveAuthorizedSummaryCount(false, 51), null, "Loading or failed summaries must render unavailable");
assert.equal(resolveAuthorizedSummaryCount(true, null), null, "Absent summaries must render unavailable");
assert.equal(resolveAuthorizedSummaryCount(true, ""), null, "Blank summaries must render unavailable");
assert.equal(resolveAuthorizedSummaryCount(true, -1), null, "Invalid negative summaries must render unavailable");
assert.equal(resolveAuthorizedSummaryCount(true, 1.5), null, "Fractional counts must render unavailable");
assert.equal(resolveAuthorizedSummaryCount(true, false), null, "Boolean values must render unavailable");
assert.equal(resolveAuthorizedSummaryCount(true, " 1"), null, "Whitespace-padded counts must render unavailable");
assert.equal(resolveAuthorizedSummaryCount(true, Number.MAX_SAFE_INTEGER + 1), null, "Unsafe integer counts must render unavailable");
assert.equal(resolveAuthorizedSummaryCount(true, "not-a-number"), null, "Invalid summary values must render unavailable");

for (const page of [liveMap, controlTower]) {
  assert.doesNotMatch(page, /Stream connected · .*valid positions/, "Transport connectivity must not certify GPS-fix currency");
  assert.doesNotMatch(page, /GPS stream live/, "A connected stream must not be described as current GPS evidence");
  assert.doesNotMatch(page, /Real-time vehicle positions|Live vehicle positions/, "Position pages must not overclaim stale fixes as current");
  assert.doesNotMatch(page, /Live Operations Map/, "Position map headings must remain freshness-neutral");
  assert.match(page, /Stream transport connected/, "Connected transport must be labeled separately from fix freshness");
  assert.match(page, /stale\/unknown/, "Position headers must disclose stale or unknown fixes");
}
assert.match(liveMap, /Last-known vehicle positions/, "Fleet map must describe positions as last-known");
assert.doesNotMatch(liveMapComponent, /Live fleet map/, "The map's accessible name must not overclaim stale positions as live");
assert.match(liveMapComponent, /Positions may be last-known/, "The map's accessible name must disclose position currency");
assert.match(
  liveMapComponent,
  /markerElement\.setAttribute\("aria-label", markerAccessibleName\)/,
  "Every rendered vehicle marker must receive its current accessible name",
);
assert.equal(
  buildVehicleMarkerAccessibleName({
    label: "WESTHUB-V-0199",
    fallbackId: "vehicle-199",
    driver: "Unassigned",
    freshness: "Stale",
    operationalStatus: "Stale",
    speedMph: 0,
  }),
  "Vehicle WESTHUB-V-0199, position stale, status stale, driver Unassigned, 0 miles per hour",
  "Vehicle-marker names must identify the vehicle and disclose freshness and operational status",
);
assert.equal(
  buildVehicleMarkerAccessibleName({
    label: "Vehicle",
    fallbackId: "vehicle-42",
    driver: "",
    freshness: "",
    operationalStatus: "",
  }),
  "Vehicle vehicle-42, position unknown, status unknown, driver unassigned",
  "Generic marker labels must fall back to a stable identifier rather than producing duplicate controls",
);
assert.doesNotMatch(
  controlTower,
  /(?:onlineDevices|onlineCameras|highRiskUnits|speedAlerts)\s*\?\?\s*0/,
  "Missing control-tower evidence must remain unavailable rather than becoming a reported zero",
);
assert.doesNotMatch(controlTower, /kpis\.onlineCameras/, "Default-contaminated camera status must not be presented as online evidence");
assert.match(controlTower, /canViewDeviceEvidence = hasPermission\(PERMISSIONS\.TELEMATICS_DEVICES_VIEW\)/, "Control Tower device evidence must use the dedicated permission");
assert.match(controlTower, /canViewDeviceEvidence && <KpiCard label="Online Device Evidence"/, "Online-device evidence must be hidden without device permission");
assert.doesNotMatch(controlTower, /Device offline/, "Control Tower must not present the legacy vehicle default as an offline-device fact");
assert.match(controlTower, /Verified Camera Evidence/, "Control Tower must label the camera collection as verified evidence");
assert.match(controlTower, /Only provider-authoritative records with media marked ready appear here/, "Control Tower must disclose the camera evidence threshold");
assert.match(controlTower, /provider, media, privacy, device, and certification gates remain on external hold/, "An empty camera collection must preserve the external hold");
assert.doesNotMatch(controlTower, /aiSummary \|\| event\.eventType/, "Unverified AI camera summaries must not be presented as evidence");
assert.match(vehicles, /No provider-verified, media-ready camera evidence is available for this vehicle/, "Vehicle detail must distinguish absent verified media from no camera events");
assert.match(controlTower, /summarizeControlTowerStatus/, "Aggregate status must use the tested evidence summary");
assert.match(commandCenter, /Current Exception Queue/, "Command Center must describe persisted exceptions without a live-data claim");
assert.match(commandCenter, /Fleet status evidence unavailable/, "Missing fleet snapshot evidence must remain visibly unavailable");
assert.match(commandCenter, /Needs Service/, "The vehicle-state slice must describe service attention rather than inferred device connectivity");
assert.match(commandCenter, /Open Control Tower/, "The dashboard alert action must lead to the current telemetry evidence surface");
assert.doesNotMatch(commandCenter, /Live Exception Queue|live fleet status|ready to respond|key: "offline"/, "Command Center must not imply live device or readiness evidence from vehicle defaults");
assert.match(alertsCenter, /Persisted telemetry alerts generated by the available ingest and detection services/, "Alerts Center must identify its persisted operational source");
assert.match(alertsCenter, /Missing records remain unavailable/, "Alerts Center must disclose missing evidence rather than fill it");
assert.match(alertsCenter, /\{filtered\.length \? filtered\.map/, "Resolved alerts must remain visible when the status filter selects them");
assert.doesNotMatch(
  alertsCenter,
  /Live alerts command room|live backend|live queue|Live lanes|Live operating pressure|no demo queue|no fake feed|No demo fallback/i,
  "Alerts Center must not market database records as live or make unprovable no-demo claims",
);
assert.match(aiCopilot, /Availability checked on request/, "Copilot must not claim readiness before checking the configured provider");
assert.match(aiCopilot, /No generated answer or substitute recommendation was created/, "Copilot failures must fail visibly without synthetic answers");
assert.match(aiCopilot, /Current persisted records within your account and branch access/, "Copilot evidence must disclose its persisted authorization scope");
assert.doesNotMatch(aiCopilot, /Operations Copilot ready|Live evidence|Evidence is pulled from live fleet data/, "Copilot UI must not make unverified readiness or live-evidence claims");
assert.doesNotMatch(
  liveMap,
  /kpis\.(?:liveCoverage|connectedUnits|degradedUnits|deviceOfflineUnits|cameraOfflineUnits|connectivityCoverage)/,
  "Fleet map must not present receipt-age or default-contaminated connectivity KPIs as fix evidence",
);
assert.match(liveMap, /positionFreshness\.recent \/ positionFreshness\.located/, "Recent-fix coverage must use authoritative fix freshness");
assert.match(liveMap, /Avg receipt age/, "Pipeline receipt age must not be labeled as device-fix age");
assert.match(moduleConfig, /title: "Fleet Position Map"/, "Navigation must use a freshness-neutral map title");
assert.match(moduleConfig, /Last-known fleet positions with fix freshness/, "Navigation copy must disclose fix freshness");

assert.deepEqual(
  summarizePositionFreshness([
    { lat: 43.1, lng: -79.1, freshness: "live", secondsSincePing: 9999 },
    { lat: 43.2, lng: -79.2, secondsSincePing: 400 },
    { lat: 43.3, lng: -79.3, isStale: true, secondsSincePing: 1 },
    { lat: 43.4, lng: -79.4 },
    { lat: 0, lng: 0, freshness: "live" },
  ]),
  { located: 4, live: 1, delayed: 1, recent: 2, stale: 1, offline: 1, staleOrUnknown: 2 },
  "Freshness summary must prefer server evidence and exclude invalid coordinates",
);

assert.deepEqual(
  summarizeControlTowerStatus({ highRiskUnits: 0, alertCount: 0, actionCount: 0, alertsAvailable: true }),
  {
    evidenceIncomplete: false,
    isNominal: true,
    isCritical: false,
    label: "No Current Exceptions Reported",
    details: "No high-risk units, open telemetry alerts, or queued actions in the current authorized scope.",
  },
  "Nominal exception state requires complete zero-valued evidence",
);
const queuedStatus = summarizeControlTowerStatus({ highRiskUnits: 0, alertCount: 0, actionCount: 2, alertsAvailable: true });
assert.equal(queuedStatus.isNominal, false, "Queued actions must prevent a nominal status");
assert.equal(queuedStatus.label, "Review Needed", "Queued actions must produce a review state");
assert.match(queuedStatus.details, /2 queued actions/, "Queued actions must be disclosed");

for (const input of [
  { highRiskUnits: null, alertCount: 0, actionCount: 0, alertsAvailable: true },
  { highRiskUnits: 0, alertCount: 0, actionCount: 0, alertsAvailable: false },
  { highRiskUnits: false, alertCount: 0, actionCount: 0, alertsAvailable: true },
  { highRiskUnits: "", alertCount: 0, actionCount: 0, alertsAvailable: true },
  { highRiskUnits: " 0", alertCount: 0, actionCount: 0, alertsAvailable: true },
  { highRiskUnits: 0, alertCount: false, actionCount: 0, alertsAvailable: true },
  { highRiskUnits: 0, alertCount: 0, actionCount: "", alertsAvailable: true },
]) {
  const status = summarizeControlTowerStatus(input);
  assert.equal(status.evidenceIncomplete, true, "Missing or malformed exception evidence must remain incomplete");
  assert.equal(status.isNominal, false, "Incomplete exception evidence must never become nominal");
  assert.equal(status.label, "Exception Evidence Incomplete", "Incomplete exception evidence must be explicit");
}

console.log("Commercial-truth copy contract passed.");
