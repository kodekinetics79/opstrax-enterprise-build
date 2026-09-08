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
const accountHealth = fs.readFileSync(new URL("../src/pages/AccountHealthPage.tsx", import.meta.url), "utf8");
const modulePage = fs.readFileSync(new URL("../src/pages/ModulePage.tsx", import.meta.url), "utf8");
const executive = fs.readFileSync(new URL("../src/pages/ExecutivePage.tsx", import.meta.url), "utf8");
const alertRules = fs.readFileSync(new URL("../src/pages/AlertRulesPage.tsx", import.meta.url), "utf8");
const analytics = fs.readFileSync(new URL("../src/pages/AnalyticsDashboardPage.tsx", import.meta.url), "utf8");
const fleetIntelligence = fs.readFileSync(new URL("../src/pages/FleetIntelligencePage.tsx", import.meta.url), "utf8");
const fleetHealth = fs.readFileSync(new URL("../src/pages/FleetHealthPage.tsx", import.meta.url), "utf8");
const fleetUtilization = fs.readFileSync(new URL("../src/pages/FleetUtilizationPage.tsx", import.meta.url), "utf8");
const compliance = fs.readFileSync(new URL("../src/pages/CompliancePage.tsx", import.meta.url), "utf8");
const operatingModule = fs.readFileSync(new URL("../src/pages/OperatingModulePage.tsx", import.meta.url), "utf8");
const fleetOverview = fs.readFileSync(new URL("../src/pages/FleetOverviewPage.tsx", import.meta.url), "utf8");
const reports = fs.readFileSync(new URL("../src/pages/ReportsPage.tsx", import.meta.url), "utf8");
const notificationCenter = fs.readFileSync(new URL("../src/pages/NotificationCenterPage.tsx", import.meta.url), "utf8");
const customerVisibility = fs.readFileSync(new URL("../src/pages/CustomerVisibilityPage.tsx", import.meta.url), "utf8");
const customerEta = fs.readFileSync(new URL("../src/pages/CustomerEtaPage.tsx", import.meta.url), "utf8");
const driverMessaging = fs.readFileSync(new URL("../src/pages/DriverMessagingPage.tsx", import.meta.url), "utf8");
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
  accountHealth,
  /activeContracts\s*\?\?\s*1|slaTimer:\s*String\(c\.sentAt\s*\?\s*"Live"|Derived from live customer and contract state|upsellOpportunity:\s*\/ftl/i,
  "Customer-success pages must not manufacture contracts, support tickets, follow-ups, or upsell opportunities",
);
assert.match(accountHealth, /Follow-up workflow unavailable/, "Missing follow-up workflow must be explicit");
assert.match(accountHealth, /Customer communication records are not support tickets/, "Communications must not be relabeled as support tickets");
assert.match(accountHealth, /does not infer sales opportunities/, "Missing upsell workflow must remain unavailable");
assert.match(accountHealth, /Persisted customer health scores, SLA evidence and at-risk status/, "Account health copy must identify its persisted evidence scope");
assert.doesNotMatch(modulePage, /Operational recommendations will surface as live events/, "Empty module insight panels must not promise live recommendations");
assert.match(modulePage, /No recorded recommendations are available for this module/, "Empty module insight panels must disclose missing recommendations");
assert.doesNotMatch(executive, /useExecutiveSnapshots|useExecutiveAiRecs|AI Live Monitoring|emptySummary/, "Executive UI must not present seeded snapshots, recommendations, or missing-data zeros as current evidence");
assert.match(executive, /Evidence-qualified record counts in the authorized tenant scope/, "Executive UI must disclose the source and authorization scope of its metrics");
assert.match(executive, /do not certify provider, device, or regulatory evidence/, "Executive record counts must not be presented as certification evidence");
assert.match(alertRules, /Enabled status records configuration intent/, "Alert rules must distinguish configuration from execution evidence");
assert.doesNotMatch(alertRules, /Triggered Today|Last Triggered|live alert-control|No live alert rules/i, "Alert rules must not show unwritten execution counters or claim live enforcement");
assert.match(analytics, /Missing denominators and unmeasured scores remain unavailable/, "Analytics must disclose missing evidence");
assert.doesNotMatch(analytics, /computed from live fleet data|Live KPIs/, "Persisted analytics must not be marketed as live telemetry");
assert.match(fleetIntelligence, /Missing measurements remain unavailable/, "Fleet Intelligence must disclose missing measurements");
assert.doesNotMatch(fleetIntelligence, /OBD \/ J1939 live|Live Telematics Alerts|every figure pulled live|value=\{offline \? "offline" : "online"\}/, "Fleet Intelligence must not infer live connectivity or diagnostic clearance");
assert.match(fleetHealth, /Missing or unmeasured safety evidence is not treated as a normal result/, "Fleet Health must not interpret missing driver evidence as normal");
assert.match(fleetHealth, /Fleet health score unavailable until qualified evidence covers the current fleet/, "Fleet Health must keep incomplete score coverage visibly unavailable");
assert.match(fleetHealth, /an empty risk list does not confirm that vehicles and drivers are within acceptable parameters/, "Fleet Health must not treat an empty qualified queue as fleet-wide clearance when coverage is incomplete");
assert.match(fleetHealth, /System Fleet Insight — rule-based guidance from evidence-qualified operational records/, "Fleet Health must disclose the source boundary of its guidance");
assert.doesNotMatch(fleetHealth, /live operational data|All drivers within normal parameters|All vehicles within normal parameters|metrics\.deviceOffline|safetyScore, 100/, "Fleet Health must not fabricate live, connectivity, normal, or perfect-score evidence");
assert.match(fleetUtilization, /No qualified trip-hour evidence is available/, "Fleet Utilization must disclose missing trip evidence");
assert.match(fleetUtilization, /readiness evidence unavailable/, "Fleet Utilization must disclose missing readiness evidence");
assert.match(fleetUtilization, /An empty queue does not prove/, "Fleet Utilization must not treat an empty action queue as clearance");
assert.doesNotMatch(fleetUtilization, /deployabilityScore|riskScore|Export live view|No idle drag detected|all within range/i, "Fleet Utilization must not manufacture readiness, risk, or healthy zero-value claims");
assert.match(compliance, /do not certify regulatory compliance/, "Compliance recommendations must retain the certification boundary");
assert.doesNotMatch(compliance, /recommendations based on live fleet data|No cross-border issues found/, "Compliance must not overstate record absence or evidence currency");
assert.match(operatingModule, /No connected production records/, "Unwired operating modules must disclose the absent data source");
assert.match(operatingModule, /value=\{hasConnectedRecords \? kpi\.value : "—"\}/, "Unwired operating modules must suppress fixed KPI values");
assert.match(operatingModule, /Operational feeds not connected/, "Operations dashboard must disclose its unavailable production feeds");
assert.match(operatingModule, /Pricing source not connected/, "Price simulation must remain unavailable without persisted pricing inputs");
assert.match(operatingModule, /Dispatch source not connected/, "Dispatch recommendations must remain unavailable without persisted eligibility inputs");
assert.doesNotMatch(
  operatingModule,
  /RTE-KSA-018|RTE-US-DC-006|KSA-REEFER-214|On-Time Delivery %", "94\.6%|Recommended Matches" value="3"|Dispatch Readiness" value="87%"|Convert to Quotation|function MapPreview/,
  "Operating workspaces must not display simulated route, vehicle, service-level, pricing, or dispatch evidence",
);
assert.doesNotMatch(fleetOverview, /No open alerts — all clear/, "An empty alert result must not be presented as proof that the fleet is clear");
assert.match(fleetOverview, /No open alert records in the current result/, "Fleet overview must describe an empty alert query without an all-clear claim");
assert.match(reports, /reports from persisted fleet records/, "Reports must describe their persisted data source");
assert.match(notificationCenter, /Persisted notifications, escalations and acknowledgements/, "Notification Center must describe persisted records");
assert.doesNotMatch(customerVisibility, /real-time ETA/i, "Customer Visibility must not claim real-time ETA without freshness evidence");
assert.match(customerVisibility, /recorded ETA evidence from available dispatch and telemetry sources/, "Customer Visibility must state its ETA evidence boundary");
assert.doesNotMatch(customerEta, /Real-time delivery visibility|Real-time driver location/, "Customer ETA must not claim real-time records without freshness evidence");
assert.match(customerEta, /Queued messages are not presented as provider-delivered/, "Customer ETA must disclose the provider-delivery boundary");
assert.match(customerEta, /Bulk Queue Updates/, "Bulk ETA actions must be labeled as queued work");
assert.match(customerEta, /No SLA assessment is available/, "Missing public SLA evidence must remain unavailable");
assert.doesNotMatch(customerEta, /customerExperienceScore|customer_experience_score|Experience Score|ETA updates sent to all|\?\? "High"/, "Customer ETA must not synthesize experience, delivery, or confidence claims");
assert.match(driverMessaging, /Record In-App Message/, "Driver messaging must describe the persisted action without claiming provider delivery");
assert.match(driverMessaging, /Delivery outside the in-app conversation is not claimed/, "Driver messaging must preserve its delivery boundary");
assert.match(driverMessaging, /active driver accounts in your authorized branch scope/, "Broadcast recipients must disclose their persisted authorization scope");
assert.match(driverMessaging, /unwrap<AnyRecord\[]>\(apiClient\.get\("\/api\/driver-messages"\)\)/, "Driver messaging must unwrap the API envelope before calculating evidence counts");
assert.doesNotMatch(driverMessaging, /status="Healthy"|<option>SMS<\/option>|Broadcast sent to all drivers|Depot — Morning Shift|Long-Haul Drivers/, "Driver messaging must not show synthetic health, external channels, or unmodeled recipient segments");
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
