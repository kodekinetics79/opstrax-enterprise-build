import assert from "node:assert/strict";
import fs from "node:fs";

const devices = fs.readFileSync(new URL("../src/pages/IotDevicesPage.tsx", import.meta.url), "utf8");
const command = fs.readFileSync(new URL("../src/pages/TelematicsCommandPage.tsx", import.meta.url), "utf8");
const importer = fs.readFileSync(new URL("../src/components/EntityImportExport.tsx", import.meta.url), "utf8");
const service = fs.readFileSync(new URL("../src/services/telematicsService.ts", import.meta.url), "utf8");
const controlTower = fs.readFileSync(new URL("../src/pages/TelematicsControlTowerPage.tsx", import.meta.url), "utf8");

assert.match(devices, /aria-label={`Manage \${row\.deviceName}`}/, "Manage identifies the selected device");
assert.match(devices, /aria-haspopup="dialog"[\s\S]*onClick=\{\(\) => setSelectedId\(row\.id\)\}/, "Manage opens the durable detail/action drawer");
assert.doesNotMatch(devices, /setOpenMenuId/, "Manage must not rely on an overflow-clipped popover");

assert.match(devices, /tab === "diagnostics"[\s\S]*Diagnostics evidence is separate from device inventory/, "Diagnostics has an evidence-specific landing state");
assert.match(devices, /Device Health does not infer diagnostic coverage for every registered device/, "Diagnostics does not mislabel inventory rows as evidence");
assert.match(devices, /disabled=\{!canDiagnostics\}[\s\S]*navigate\("\/obd-j1939"\)/, "Diagnostics navigation is permission-aware");

assert.match(command, /apiErrorMessage\(recordsQ\.error, fallback\)/, "Telemetry load errors preserve safe server guidance");
assert.match(command, /OBD \/ J1939 evidence could not be loaded[\s\S]*confirm diagnostics access for this role/, "OBD fallback gives the Executive a useful recovery path");
assert.match(command, /canViewGeofences = hasPermission\("map:view"\)/, "Geofence discovery follows the destination permission");
assert.match(command, /kind === "gps-tracking"[\s\S]*navigate\("\/geofences"\)[\s\S]*Manage Geofences/, "GPS exposes geofence management");
assert.match(service, /serialNumber: device\.serialNumber/, "GPS cluster retains the immutable serial for same-model device uniqueness");
assert.match(command, /row\.serialNumber[\s\S]*row\.deviceName/, "GPS renders serial prominently and model secondarily");
assert.match(command, /"obd-j1939"[\s\S]*columns: \["serialNumber", "vehicleCode", "deviceName"/, "OBD/J1939 table leads with immutable device serial");
assert.match(command, /canViewDevices = hasPermission\(PERMISSIONS\.TELEMATICS_DEVICES_VIEW\)[\s\S]*canViewVehicles = hasPermission\(PERMISSIONS\.VEHICLES_VIEW\)[\s\S]*canViewJobs = hasDirectPermission\(PERMISSIONS\.SHIPMENTS_VIEW\)[\s\S]*canViewMap = hasPermission\(PERMISSIONS\.TELEMETRY_LIVE_STATE_READ\)/, "Telemetry drilldown controls follow their destination permissions");
assert.match(command, /Device serial", row\.serialNumber[\s\S]*Device model", row\.deviceName/, "Telemetry detail keeps immutable serial primary and model secondary");
assert.match(controlTower, /device: device\.serialNumber, model: device\.deviceName/, "Control Tower separates immutable serial from display model");
assert.match(controlTower, /Export full device inventory/, "Control Tower export states that it is not the filtered queue");
assert.match(controlTower, /canViewDevices = hasPermission\(PERMISSIONS\.TELEMATICS_DEVICES_VIEW\)/, "Control Tower checks Device Health destination access");
assert.match(controlTower, /canViewGps = hasPermission\(PERMISSIONS\.TELEMATICS_GPS_VIEW\)/, "Control Tower checks GPS destination access");
assert.match(controlTower, /canViewDiagnostics = hasPermission\(PERMISSIONS\.TELEMATICS_DIAGNOSTICS_VIEW\)/, "Control Tower checks diagnostics destination access");
assert.match(controlTower, /\{canViewGps \? <button[\s\S]*GPS<\/button> : null\}/, "Control Tower hides GPS navigation when permission is absent");
assert.match(controlTower, /\{canViewDiagnostics \? <button[\s\S]*Diagnostics<\/button> : null\}/, "Control Tower hides diagnostics navigation when permission is absent");
assert.match(controlTower, /\{canViewDevices \? <button[\s\S]*Device Health<\/button> : null\}/, "Control Tower hides Device Health navigation when permission is absent");
assert.match(command, /Fleet managed units[\s\S]*Fleet offline \/ stale[\s\S]*Fleet needs action[\s\S]*Current page health/, "GPS KPI labels state fleet and page scope truthfully");
assert.match(command, /Fleet cards cover every authorized unit[\s\S]*current page/, "GPS explains mixed KPI scopes");
assert.match(command, /Search serial, IMEI, model, category, provider, vehicle, driver, or location/, "GPS search promise matches backend-supported fields");
assert.match(command, /Delayed \/ Watch[\s\S]*delayed-gps/, "GPS exposes the delayed-fix cohort");
assert.match(command, /Sort telemetry records[\s\S]*Highest risk first[\s\S]*Freshness risk[\s\S]*Latest fix first[\s\S]*Device serial/, "GPS exposes enterprise sort controls");
assert.match(service, /sort: options\.sort \?\? "risk"[\s\S]*direction: options\.direction \?\? "desc"/, "Telemetry sort reaches the server page endpoint");
assert.match(command, /columnLabels\[column\] \?\? column/, "GPS uses human-readable column labels");
assert.match(command, /sticky left-0[\s\S]*sticky right-0/, "GPS keeps identity and actions visible during horizontal scroll");

assert.match(devices, /entity: "device installations"[\s\S]*atomic: true/, "Device Health exposes an atomic bulk installation wizard");
assert.match(devices, /canBulkInstall = hasDirectPermission\(PERMISSIONS\.TELEMETRY_DEVICES_MANAGE\)/, "Bulk installation UI requires the direct canonical device-manage grant");
assert.match(devices, /toolbarLabel: "Device"[\s\S]*toolbarLabel: "Installation"/, "Adjacent device and installation import controls have distinct visible labels");
assert.match(devices, /This create-only workflow records new installations[\s\S]*Exact idempotent replays are skipped[\s\S]*governed Transfer action/, "Installation wizard describes create-only, replay, and reassignment semantics truthfully");
assert.match(devices, /deviceSerial[\s\S]*vehicleCode[\s\S]*effectiveFrom[\s\S]*idempotencyKey/, "Bulk installation uses governed identity, time, and replay fields");
assert.match(service, /device-installations\/import-preview/, "Bulk installation preview uses the governed API");
assert.match(service, /device-installations\/import-commit[\s\S]*timeout: 120000/, "Bulk installation commit has a bounded large-batch timeout");
assert.match(importer, /action: "create" \| "update" \| "skip" \| "error"/, "Already-recorded rows have a neutral preview state");
assert.match(importer, /config\.atomic === true && invalid > 0/, "Atomic imports cannot commit a known-invalid preview");
assert.match(command, /exportTelemetryClusterCsv\(kind,[\s\S]*Export every authorized row matching the current search and filter/, "Paged export fetches the complete authorized result set");
assert.match(devices, /Revoke & Archive[\s\S]*Use Suspend for a reversible stop/, "Permanent credential revocation is not mislabeled as reversible archive");

assert.match(service, /pageSize: 10_000[\s\S]*purpose: "export"[\s\S]*new Set\(identities\)\.size[\s\S]*exportComplete/, "Cluster export uses one bounded snapshot and fails closed on duplicate or incomplete identities");
assert.match(service, /purpose: "export"/, "Cluster export declares its server-enforced export purpose");
assert.match(service, /\^\[=\+\\-@\\t\\r\]/, "Cluster CSV neutralizes spreadsheet formulas");

console.log("Telematics customer-pilot UX contract passed.");
