import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, resolve } from "node:path";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const service = readFileSync(resolve(root, "src/services/telematicsService.ts"), "utf8");
const devicesPage = readFileSync(resolve(root, "src/pages/IotDevicesPage.tsx"), "utf8");
const vehiclesPage = readFileSync(resolve(root, "src/pages/VehiclesPage.tsx"), "utf8");

assert.match(service, /imei: String\(row\.imei \?\? ""\)/, "IMEI must be mapped from the live device row");
assert.doesNotMatch(service, /imei:\s*""/, "Device mapping must not erase IMEI readback");
assert.match(service, /detail\.current_installation/, "Device detail must consume the current installation");
assert.match(service, /detail\.installation_history/, "Device detail must consume installation history");
assert.match(service, /detail\.assignment_history/, "Device detail must consume assignment history");
assert.match(service, /function deviceRowFromDetail/, "Single-device readbacks must unwrap the nested detail envelope");
assert.match(service, /const created = deviceRowFromDetail/, "Provision readback must preserve the returned device identity fields");
assert.match(service, /\/installations\/\$\{current\.id\}\/remove/, "Removal must use the installation endpoint");
assert.match(service, /expectedRowVersion: current\.rowVersion/, "Removal must send optimistic concurrency state");
assert.match(service, /idempotencyKey: installationMutationKey\(deviceId\)/, "Installation must be idempotent");
assert.match(service, /\/installations\/transfer/, "Transfer must use the atomic transfer endpoint");
assert.match(service, /result: "Passed"/, "Commissioning must send the canonical successful result");
assert.match(service, /activationVerifiedAt: row\.activation_verified_at/, "Activation heartbeat evidence must be mapped from the installation contract");
assert.match(service, /if \(!current\.activationVerifiedAt\)/, "Commissioning must reject installations without activation evidence");
assert.match(service, /expectedRowVersion: current\.rowVersion/, "Commissioning must send installation concurrency state");
const provisionRequest = service.match(/apiClient\.post\("\/api\/telemetry\/devices\/provision", \{[\s\S]*?\}\)\)/)?.[0] ?? "";
assert.ok(provisionRequest, "Provision request contract must remain identifiable");
assert.doesNotMatch(provisionRequest, /vehicleId|driverId/, "Provisioning must never bind a vehicle or driver");
const connectForm = devicesPage.match(/type ConnectFormState = \{[\s\S]*?\};/)?.[0] ?? "";
assert.doesNotMatch(connectForm, /assignedVehicleId|vehicleId/, "Connection form must not combine provisioning with installation");
assert.doesNotMatch(devicesPage, /Metadata edits were captured/, "Unsupported metadata must not report success");
assert.match(devicesPage, /Metadata read-only/, "Unsupported metadata must be labelled read-only");
assert.match(devicesPage, /Installation History/, "The detail drawer must render installation history");
assert.match(devicesPage, /Device lifecycle action failed/, "Lifecycle mutation failures must be rendered to the operator");
assert.doesNotMatch(vehiclesPage, /withDeviceEvidence/, "Vehicle health must not overwrite the API-selected primary installation with an unordered client-side device join");
assert.match(devicesPage, /authenticated device heartbeat must verify activation/, "Commissioning must expose its activation-evidence gate");
assert.match(vehiclesPage, /if \(deviceId == null\) return "Unknown"/, "Vehicles without a current device must be Unknown");
assert.match(vehiclesPage, /if \(lastSeen == null\) return "Disconnected"/, "Installed devices without live evidence must be Disconnected");
assert.doesNotMatch(vehiclesPage, /telematicsService\.getDevices/, "Vehicle list health must not overwrite the backend-selected primary installation");
assert.match(vehiclesPage, /scopeRowsForSession\("vehicles", pagedRows, session\)/, "Vehicle list health must consume the tenant-scoped authoritative vehicle response");
assert.match(vehiclesPage, /const currentDevices = \(detail\?\.currentDevices/, "Vehicle detail must render authoritative current installations");
assert.match(vehiclesPage, /selectedRecord = selectedDetailRecord/, "Vehicle health must retain the API-selected primary installation on detail refresh");
assert.match(vehiclesPage, /15 \* 60_000/, "Device health freshness must use the service heartbeat window");
for (const field of ["vinExceptionType", "alternateIdentifier", "plateJurisdiction", "vehicleClass"]) {
  assert.match(vehiclesPage, new RegExp(`key: "${field}"`), `${field} must be available in create/edit`);
}
assert.match(vehiclesPage, /approved alternate identity kind/, "VIN-less vehicles must be client-validated against governed identity requirements");
assert.match(vehiclesPage, /label: "Alternate identity"/, "Vehicle detail must render the governed alternate identity");

const driverPage = readFileSync(resolve(root, "src/pages/driver/DriverAssignmentPage.tsx"), "utf8");
assert.match(driverPage, /s !== "accepted" \|\| status === "exception"/, "Initial acceptance must use the canonical endpoint while exception recovery remains executable");
assert.match(driverPage, /Resume Assignment/, "Accepted assignments must be able to resume from exception without a duplicate initial accept action");
assert.match(driverPage, /s === "assigned" \|\| s === "accepted"/, "Pre-accept and post-accept exceptions must both expose the governed resume action");
for (const [status, label] of [
  ["en_route_pickup", "Start Route to Pickup"],
  ["arrived_pickup", "Mark Arrived at Pickup"],
  ["loaded", "Mark Loaded"],
  ["in_transit", "Mark In Transit"],
  ["arrived_delivery", "Mark Arrived at Delivery"],
]) {
  assert.match(driverPage, new RegExp(`${status}:\\s+"${label}"`), `${status} must describe the transition it executes`);
}

const dvirPage = readFileSync(resolve(root, "src/pages/DvirInspectionsPage.tsx"), "utf8");
assert.match(dvirPage, /New checklist template/, "Fleet operators must be able to create the driver checklist prerequisite");
assert.match(dvirPage, /dvirApi\.createTemplate/, "Checklist template UI must persist through the real API");
assert.match(dvirPage, /checklistItems:/, "Checklist template creation must submit its persisted item rows");

const driverDashboard = readFileSync(resolve(root, "src/pages/driver/DriverDashboardPage.tsx"), "utf8");
assert.match(driverDashboard, /latestPretripSafeToOperate/, "Driver home must consume the same signed pre-trip evidence as the trip page");
assert.match(driverDashboard, /vehicleConfirmedAt/, "Driver home must consume exact-vehicle confirmation before declaring departure ready");
assert.match(driverDashboard, /Verify assigned vehicle/, "Driver home must distinguish missing vehicle confirmation from missing DVIR evidence");
assert.match(driverDashboard, /Start route to pickup/, "Driver home must not request a duplicate DVIR after signed safe evidence exists");

console.log("device installation frontend contract: ok");
