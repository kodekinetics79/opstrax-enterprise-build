import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, resolve } from "node:path";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const service = readFileSync(resolve(root, "src/services/telematicsService.ts"), "utf8");
const devicesPage = readFileSync(resolve(root, "src/pages/IotDevicesPage.tsx"), "utf8");
const vehiclesPage = readFileSync(resolve(root, "src/pages/VehiclesPage.tsx"), "utf8");
const coldChainPage = readFileSync(resolve(root, "src/pages/FleetColdChainPage.tsx"), "utf8");

assert.match(service, /imei: String\(row\.imei \?\? ""\)/, "IMEI must be mapped from the live device row");
assert.match(service, /deviceCategory: String\(row\.device_category \?\? "Unknown"\)/, "Governed hardware category must be mapped from the live device row");
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
assert.match(service, /\/api\/telemetry\/installation-quarantine/, "Identity conflicts must use the governed quarantine API");
assert.match(service, /resolveIdentityQuarantine/, "Operators must have a supported quarantine resolution mutation");
assert.doesNotMatch(service, /result:\s*"Passed"\s*,/, "Commissioning must never hardcode a successful result");
assert.match(service, /result: input\.result/, "Commissioning must send the operator-observed result");
assert.match(service, /verificationReference,/, "Commissioning must send the operator-entered evidence reference");
assert.match(service, /activationVerifiedAt: row\.activation_verified_at/, "Activation heartbeat evidence must be mapped from the installation contract");
for (const field of ["odometer_at_installation", "commissioning_method", "commissioning_result", "verification_reference", "removal_reason"]) {
  assert.match(service, new RegExp(`row\\.${field}`), `${field} must be mapped back into installation history`);
}
assert.match(service, /input\.result === "Passed" && !current\.activationVerifiedAt/, "Only Passed commissioning must require activation evidence");
assert.match(service, /expectedRowVersion: current\.rowVersion/, "Commissioning must send installation concurrency state");
assert.doesNotMatch(service, /deviceRole:\s*"GPS"/, "Installation must not hardcode the device role");
assert.doesNotMatch(service, /isPrimary:\s*true/, "Installation must not hardcode primary status");
assert.match(service, /deviceRole: input\.deviceRole\.trim\(\)/, "Installation must send the operator-selected role");
assert.match(service, /isPrimary: input\.isPrimary/, "Installation must send the operator-selected primary designation");
for (const field of ["effectiveAt", "installationLocation", "odometerAtInstallation", "commissioningMethod", "assignmentReason", "removalReason"]) {
  assert.match(service, new RegExp(`input\\.${field}`), `${field} must come from operator input`);
}
assert.doesNotMatch(service, /Removed by fleet operator|Transferred to another vehicle|Operator installation/, "Installation reasons must not be generic hardcoded claims");
assert.match(service, /\/api\/telemetry\/devices\/\$\{deviceId\}\/suspend/, "Device suspension must use the persisted backend endpoint");
assert.match(service, /\/api\/telemetry\/devices\/\$\{deviceId\}\/activate/, "Device activation must use the persisted backend endpoint");
assert.match(service, /\/api\/telemetry\/devices\/\$\{deviceId\}\/rotate-secret/, "Credential rotation must use the one-time backend contract");
const provisionRequest = service.match(/apiClient\.post\("\/api\/telemetry\/devices\/provision", \{[\s\S]*?\}\)\)/)?.[0] ?? "";
assert.ok(provisionRequest, "Provision request contract must remain identifiable");
assert.doesNotMatch(provisionRequest, /vehicleId|driverId/, "Provisioning must never bind a vehicle or driver");
assert.match(provisionRequest, /deviceCategory,/, "Provisioning must send the explicitly selected governed hardware category");
const connectForm = devicesPage.match(/type ConnectFormState = \{[\s\S]*?\};/)?.[0] ?? "";
assert.doesNotMatch(connectForm, /assignedVehicleId|vehicleId/, "Connection form must not combine provisioning with installation");
assert.match(connectForm, /deviceCategory: string/, "Connection form must collect a governed hardware category");
assert.match(devicesPage, /Hardware category \(required\)/, "Device onboarding must require a governed hardware category selector");
assert.match(devicesPage, /form\.deviceCategory\.trim\(\)\.length > 0/, "Device connection must be blocked until category is selected");
for (const label of ["Device role (required)", "Vehicle role designation (required)", "Installation location (required)", "Odometer at installation (required)", "Commissioning method (required)", "Assignment reason (required)", "Observed result (required)", "Evidence / failure reference (required)"]) {
  assert.match(devicesPage, new RegExp(label.replace(/[()]/g, "\\$&")), `${label} must be collected explicitly`);
}
assert.match(devicesPage, /defaultCommissioningForm:[^=]*= \{ result: ""/, "Commissioning must require an explicit result rather than defaulting a claim");
assert.doesNotMatch(devicesPage, /defaultCommissioningForm:[^=]*= \{ result: "(?:Passed|Failed)"/, "Commissioning must not default either outcome");
assert.match(devicesPage, /Replacement credentials/, "Rotated secrets must be shown through an explicit one-time dialog");
assert.match(devicesPage, /setRotatedCredentials\(null\)/, "One-time rotated secrets must be cleared when the dialog closes");
assert.match(devicesPage, /visible: canManageLifecycle/, "device lifecycle actions must carry an explicit server-permission visibility gate");
assert.match(devicesPage, /visible: canAssign/, "device installation actions must carry an explicit assignment visibility gate");
assert.match(devicesPage, /visible: canDelete/, "device archival must carry an explicit delete visibility gate");
assert.match(devicesPage, /const canCreate = canManageDeviceLifecycle/, "device onboarding must use the API's telemetry.devices.manage contract");
assert.match(devicesPage, /const canDelete = canManageDeviceLifecycle/, "device archival must use the API's telemetry.devices.manage contract");
assert.match(devicesPage, /const canGovernInstallations = canManageDeviceLifecycle/, "device installation must use the API's telemetry.devices.manage contract");
assert.match(devicesPage, /actionContracts\.filter\(\(contract\) => contract\.visible\)/, "permission-blocked device mutations must be omitted from the detail UI");
assert.match(devicesPage, /\{connectOpen && canCreate \? \(/, "a stale device connection dialog must close when permission is lost");
assert.match(devicesPage, /\{assignTarget && canGovernInstallations \? \(/, "a stale installation dialog must close when permission is lost");
assert.match(devicesPage, /\{confirmTarget && confirmAllowed \? \(/, "destructive confirmation must fail closed if permission changes");
assert.doesNotMatch(devicesPage, /Schedule firmware for/, "Unsupported OTA scheduling must not be presented as an operational form");
assert.doesNotMatch(devicesPage, /onRunDiagnostics|diagnosticsMut/, "Unsupported on-demand diagnostics must not be presented as an operational action");
assert.match(devicesPage, /OTA scheduling and firmware history are not connected/, "Unsupported OTA must be labelled explicitly as read-only");
assert.doesNotMatch(devicesPage, /Metadata edits were captured/, "Unsupported metadata must not report success");
assert.match(devicesPage, /Metadata read-only/, "Unsupported metadata must be labelled read-only");
assert.match(devicesPage, /Installation History/, "The detail drawer must render installation history");
assert.match(devicesPage, /Device lifecycle action failed/, "Lifecycle mutation failures must be rendered to the operator");
assert.match(devicesPage, /\{ key: "archived", label: "Archived" \}/, "Archived devices must remain available in an explicit lifecycle view");
assert.doesNotMatch(devicesPage, /\.filter\(\(row\) => row\.lifecycleStatus !== "Archived"\)/, "Archived records must not be silently removed before lifecycle filtering");
assert.match(devicesPage, /const managedCount = devicesQ\.data\?\.summary\.active \?\? 0/, "Managed device totals must use the branch-scoped server summary and exclude archived inventory");
assert.match(devicesPage, /\["Lifecycle", device\.lifecycleStatus\]/, "Device detail must render lifecycle status");
assert.match(devicesPage, /Identity Quarantine/, "Identity quarantine must be visible without manual database identifiers");
assert.match(devicesPage, /Resolve with audit evidence/, "Quarantine resolution must retain an explicit evidence workflow");
assert.doesNotMatch(vehiclesPage, /withDeviceEvidence/, "Vehicle health must not overwrite the API-selected primary installation with an unordered client-side device join");
assert.match(devicesPage, /Passed remains gated by an authenticated device heartbeat/, "Commissioning must expose its activation-evidence gate");
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

assert.match(coldChainPage, /source: 'Manual'/, "Operator-entered cold-chain observations must be persisted with Manual provenance");
assert.match(coldChainPage, /sourceChannel: 'Operator console'/, "Operator-entered observations must identify their source channel");
assert.doesNotMatch(coldChainPage, /humidityPercent:\s*51/, "Cold-chain observations must not fabricate humidity");
assert.doesNotMatch(coldChainPage, /status:\s*'Normal'/, "The client must not fabricate a normal policy result");
assert.doesNotMatch(coldChainPage, /Manual telemetry sample created|Reviewed by operations/, "Audit evidence must not be replaced with invented notes");
assert.match(coldChainPage, /No reading/, "Devices without reported temperature evidence must render an honest empty value");
assert.match(coldChainPage, /Never reported/, "Devices without a reported timestamp must not be presented as live");

const driverPage = readFileSync(resolve(root, "src/pages/driver/DriverAssignmentPage.tsx"), "utf8");
assert.match(driverPage, /s !== "accepted" \|\| status === "exception"/, "Initial acceptance must use the canonical endpoint while exception recovery remains executable");
assert.match(driverPage, /Resume Assignment/, "Accepted assignments must be able to resume from exception without a duplicate initial accept action");
assert.match(driverPage, /s === "assigned" \|\| s === "accepted"/, "Pre-accept and post-accept exceptions must both expose the governed resume action");
assert.match(driverPage, /vehicleConfirmedByDriverId/, "Trip UI must consume the vehicle confirmer identity");
assert.match(driverPage, /String\(assignment\["vehicleConfirmedByDriverId"\]\) === String\(payload\["driverId"\]\)/, "Trip UI must require confirmation by the authenticated driver");
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
assert.match(driverDashboard, /vehicleConfirmedByDriverId/, "Driver home must require confirmation by the authenticated driver");
assert.match(driverDashboard, /String\(assignment\["vehicleConfirmedByDriverId"\]\) === String\(driver\["id"\]\)/, "Driver home confirmation must match the authenticated driver identity");
assert.match(driverDashboard, /Verify assigned vehicle/, "Driver home must distinguish missing vehicle confirmation from missing DVIR evidence");
assert.match(driverDashboard, /Start route to pickup/, "Driver home must not request a duplicate DVIR after signed safe evidence exists");

// ── DEF-022: submit handlers must never convert/validate inside the mutate argument ──
// A synchronous throw there is swallowed (zero network I/O, no visible error).
const assignSubmit = devicesPage.match(/assignMut\.mutate\(\{[\s\S]*?\}\);/)?.[0] ?? "";
assert.ok(assignSubmit, "Installation submit mutate call must remain identifiable");
assert.doesNotMatch(assignSubmit, /toUtcIso\(/, "Installation submit must not call toUtcIso inside the mutate argument (a throw there is silently swallowed)");
const unassignSubmit = devicesPage.match(/unassignMut\.mutate\(\{[\s\S]*?\}\);/)?.[0] ?? "";
assert.ok(unassignSubmit, "Removal submit mutate call must remain identifiable");
assert.doesNotMatch(unassignSubmit, /toUtcIso\(/, "Removal submit must not call toUtcIso inside the mutate argument");
assert.match(devicesPage, /effectiveAtIso = toUtcIso\(/, "Installation effective time must be converted before mutate, inside try/catch");
assert.match(devicesPage, /effectiveToIso = toUtcIso\(/, "Removal effective time must be converted before mutate, inside try/catch");
assert.match(devicesPage, /setFormError\(validationError instanceof Error/, "Hoisted validation failures must land in visible form-error state");
assert.match(devicesPage, /error=\{formError \?\? \(assignMut\.error instanceof Error/, "Installation form must merge validation and mutation errors into the ModalForm error slot");
assert.match(devicesPage, /error=\{formError \?\? \(unassignMut\.error instanceof Error/, "Removal form must merge validation and mutation errors into the ModalForm error slot");
assert.match(devicesPage, /error=\{formError \?\? \(installMut\.error instanceof Error/, "Commissioning form must merge validation and mutation errors into the ModalForm error slot");
assert.match(devicesPage, /setFormError\("Select the observed commissioning result/, "Commissioning submit must surface a visible error instead of a silent bare return");
assert.match(devicesPage, /Enter a valid odometer at installation/, "Odometer validation must produce a visible error instead of a silent bare return");

// ── DEF-023: destructive lifecycle confirmations must be automatable and accessible ──
const confirmDialog = readFileSync(resolve(root, "src/components/ConfirmDialog.tsx"), "utf8");
assert.doesNotMatch(devicesPage, /window\.confirm\(/, "Native window.confirm blocks automation and assistive tech; lifecycle confirmations must use ConfirmDialog");
assert.match(devicesPage, /import \{ ConfirmDialog \} from "@\/components\/ConfirmDialog";/, "The device page must import the shared accessible ConfirmDialog");
assert.match(devicesPage, /<ConfirmDialog/, "ConfirmDialog must actually be rendered for lifecycle confirmations");
for (const action of ['action: "archive"', 'action: "suspend"', 'action: "rotate-credentials"']) {
  assert.match(devicesPage, new RegExp(action.replace(/[""]/g, '"')), `${action} must confirm through the in-app dialog`);
}
assert.match(confirmDialog, /role="dialog"/, "ConfirmDialog must expose a real dialog role");
assert.match(confirmDialog, /aria-modal="true"/, "ConfirmDialog must be modal to assistive technology");
assert.match(confirmDialog, /aria-labelledby=\{titleId\}/, "ConfirmDialog must be labelled by its visible title");
assert.match(confirmDialog, /Escape/, "ConfirmDialog must cancel on Escape");
assert.match(confirmDialog, /btn-danger/, "ConfirmDialog must offer a danger-variant confirm button");
assert.match(confirmDialog, /busy \? "Working\.\.\." : confirmLabel/, "ConfirmDialog must expose an in-flight busy state");

// Focus restore. Reading document.activeElement on mount lands on <body>: the row menu
// that opened the dialog unmounts in the SAME commit that mounts it, so the opener has
// to be captured at open time and handed in.
assert.doesNotMatch(
  confirmDialog, /const previouslyFocused = document\.activeElement/,
  "ConfirmDialog must not capture the opener on mount — by then the row menu has already unmounted and activeElement is <body>",
);
assert.match(confirmDialog, /returnFocusTo\?: HTMLElement \| null/, "ConfirmDialog must accept the opener captured before it unmounted");
assert.match(confirmDialog, /opener\.isConnected/, "ConfirmDialog must not focus a detached opener (the archived row is gone from the document)");
assert.match(confirmDialog, /fallbackFocusSelector/, "ConfirmDialog must fall back to a stable page landmark when the opener is gone");
assert.match(devicesPage, /const openConfirm = /, "The device page must capture the opening control when it opens a confirmation");
assert.match(devicesPage, /opener: document\.activeElement instanceof HTMLElement/, "The opener must be captured at setConfirmTarget time, not inside the dialog");
assert.match(devicesPage, /returnFocusTo=\{confirmTarget\.opener/, "The captured opener must be handed to ConfirmDialog");
assert.doesNotMatch(devicesPage, /setConfirmTarget\(\{ action:/, "Every confirmation must open through openConfirm so the opener is captured");

// The focus trap must keep owning focus while busy. Disabling every control drops focus
// onto <body>, and Tab from <body> walks the BACKGROUND page — the trap only wraps at the
// dialog's own first/last focusable.
const cancelButton = confirmDialog.match(/<button\s+ref=\{cancelButton\}[\s\S]*?<\/button>/)?.[0] ?? "";
assert.ok(cancelButton, "ConfirmDialog cancel button must remain identifiable");
assert.doesNotMatch(cancelButton, /\bdisabled=\{busy\}/, "Cancel must stay focusable while busy, or focus escapes the dialog to <body>");
assert.match(cancelButton, /aria-disabled=\{busy/, "Cancel must be aria-disabled (not disabled) while busy so it keeps its place in the tab order");
assert.match(cancelButton, /if \(!busy\) onCancel\(\)/, "An aria-disabled Cancel must still refuse to act, so the dialog cannot detach from an in-flight action");
assert.match(
  confirmDialog, /!dialogRef\.current\?\.contains\(active\)/,
  "The Tab handler must pull focus back when activeElement has escaped the dialog",
);

// ── DEF-021: a stale server error must not reappear as though the current input caused it ──
// The dialogs render `formError ?? mutation.error`; clearing only formError un-masks the
// PREVIOUS submit's 400 in the same role="alert".
for (const [helper, mutation] of [
  ["updateInstallationForm", "assignMut"],
  ["updateRemovalForm", "unassignMut"],
  ["updateCommissioningForm", "installMut"],
]) {
  const body = devicesPage.match(new RegExp(`const ${helper} = [\\s\\S]*?\\n  \\};`))?.[0] ?? "";
  assert.ok(body, `${helper} must remain identifiable`);
  assert.match(body, new RegExp(`${mutation}\\.reset\\(\\)`), `${helper} must reset ${mutation} so an old server error cannot resurface after a later client-side error`);
}

// ── DEF-020: measured counts must not default ──
assert.match(devicesPage, /function measuredCount/, "Measured counts must render through an honest helper");
assert.doesNotMatch(devicesPage, /pendingDevices \?\? 0/, "An unreported follow-up count must render as absent, never as a confident 0");
assert.doesNotMatch(devicesPage, /deviceCount \|\| 0/, "An unreported device count must render as absent, never as a confident 0");

const auditPage = readFileSync(resolve(root, "src/pages/AuditLogsPage.tsx"), "utf8");
assert.doesNotMatch(auditPage, /rec\.score \?\? 0/, "An unscored recommendation must not be presented as scoring zero");
assert.match(auditPage, /Unscored/, "A recommendation with no score must say so");
assert.match(auditPage, /const exportsLoading\s*=/, "The export-request query's loading state must be distinguishable from a measured zero");
assert.match(auditPage, /const aiRecsUnavailable\s*=/, "The recommendation query's failure state must be distinguishable from 'none found'");
assert.match(auditPage, /Export requests could not be loaded/, "A failed export-request load must render an explicit error, not 'No export requests yet'");
assert.match(auditPage, /Operations recommendations could not be loaded/, "A failed recommendation load must render an explicit error, not 'No recommendations available'");
assert.match(auditPage, /exportsUnknown \? "—" : stats\.exportsTotal/, "The Export Requests total must render '—' while unknown, never 0");

console.log("device installation frontend contract: ok");
