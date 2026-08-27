import assert from "node:assert/strict";
import fs from "node:fs";

const devices = fs.readFileSync(new URL("../src/pages/IotDevicesPage.tsx", import.meta.url), "utf8");
const command = fs.readFileSync(new URL("../src/pages/TelematicsCommandPage.tsx", import.meta.url), "utf8");

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
assert.match(command, /exportTelemetryClusterCsv\(kind,[\s\S]*Export every authorized row matching the current search and filter/, "Paged export fetches the complete authorized result set");
assert.match(devices, /Revoke & Archive[\s\S]*Use Suspend for a reversible stop/, "Permanent credential revocation is not mislabeled as reversible archive");

const service = fs.readFileSync(new URL("../src/services/telematicsService.ts", import.meta.url), "utf8");
assert.match(service, /while \(rows\.length < expectedTotal\)/, "Cluster export traverses every server page");
assert.match(service, /purpose: "export"/, "Cluster export declares its server-enforced export purpose");
assert.match(service, /\^\[=\+\\-@\\t\\r\]/, "Cluster CSV neutralizes spreadsheet formulas");

console.log("Telematics customer-pilot UX contract passed.");
