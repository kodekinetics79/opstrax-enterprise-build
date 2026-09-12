import assert from "node:assert/strict";
import fs from "node:fs";

const read = (path) => fs.readFileSync(new URL(path, import.meta.url), "utf8");
const shell = read("../src/layouts/AppShell.tsx");
const styles = read("../src/styles/index.css");
const sharedUi = read("../src/components/ui.tsx");
const entityImportExport = read("../src/components/EntityImportExport.tsx");
const driverScorecards = read("../src/pages/DriverScorecardsPage.tsx");
const maintenance = read("../src/pages/MaintenanceCommandPage.tsx");
const admin = read("../src/pages/AdminPage.tsx");
const activeShipments = read("../src/pages/ActiveShipmentsPage.tsx");
const activeShipmentsApi = read("../src/services/activeShipmentsApi.ts");
const commandCenter = read("../src/pages/CommandCenterPage.tsx");
const activeShipmentsEndpoint = read("../../backend-dotnet/Controllers/ActiveShipmentsEndpoints.cs");
const commandCenterEndpoint = read("../../backend-dotnet/Controllers/EndpointMappings.cs");
const integrations = read("../src/pages/IntegrationsPage.tsx");
const devices = read("../src/pages/IotDevicesPage.tsx");
const safety = read("../src/pages/Batch4SafetyPage.tsx");
const platformUi = read("../src/pages/platform/ui.tsx");

assert.doesNotMatch(shell, /<WorkspaceExperience/, "The tenant shell must not restore the duplicate Quick Access strip above every module.");
assert.match(shell, /tenant-workspace/, "Tenant routes must share the compact workspace contract.");
assert.match(styles, /--control-standard:\s*32px/, "Standard desktop controls must remain compact.");
assert.doesNotMatch(styles, /any-pointer:\s*coarse/, "A secondary touchscreen must not inflate controls for a desktop primary pointer.");
assert.match(styles, /@media \(pointer:\s*coarse\), \(max-width:\s*639px\)/, "Touch-primary and narrow viewports must retain 44px targets.");
assert.match(styles, /\.page-stack\s*\{[\s\S]*?gap:\s*var\(--space-3\)/, "Pages must use the shared 12px vertical rhythm.");
assert.match(styles, /@media \(min-width:\s*640px\)[\s\S]*?\.page-header__actions[\s\S]*?\.w-full[\s\S]*?width:\s*auto/, "Only PageHeader actions should normalize legacy full-width desktop buttons.");
assert.doesNotMatch(styles, /\.tenant-workspace button\.w-full/, "The workspace contract must not shrink legitimate full-width form, drawer, card, or driver controls.");
assert.match(sharedUi, /password-field w-full pr-10/, "Password fields must reserve space for their visibility control.");
assert.match(sharedUi, /password-toggle absolute/, "Password visibility controls must use the shared touch-target hook.");
assert.match(styles, /\.password-toggle\s*\{\s*width:\s*var\(--control-touch\)/, "Password visibility controls must reach 44px on touch and narrow viewports.");
assert.match(sharedUi, /flex-nowrap items-center gap-2 overflow-x-auto sm:flex-wrap sm:overflow-visible/, "Option filter bars must scroll in one row on narrow screens and wrap on desktop.");
assert.match(sharedUi, /filter-chip-active[^\n]*filter-chip[^\n]*shrink-0/, "Filter options must remain intact inside the narrow horizontal rail.");

const importExportToolbar = entityImportExport.slice(
  entityImportExport.indexOf("export function EntityImportExport"),
  entityImportExport.indexOf("/* ---------------- wizard ---------------- */"),
);
assert.doesNotMatch(importExportToolbar, /\bh-10\b/, "Shared import/export toolbar actions must use the 32px standard control instead of a local 40px override.");
assert.match(driverScorecards, /max-w-full items-center gap-1\.5 overflow-x-auto[^>]*role="tablist"/, "Scorecard tabs must remain reachable through a narrow horizontal rail.");
assert.match(driverScorecards, /className="field w-full sm:ml-auto sm:w-48 sm:flex-none"/, "Scorecard search must stack at full width on narrow screens and remain compact on desktop.");

assert.match(maintenance, /location\.pathname === "\/work-orders"[\s\S]*?"Work Orders"/, "The Work Orders route must open the Work Orders tab.");
assert.match(maintenance, /location\.pathname === "\/inspections"[\s\S]*?"Inspections"/, "The Inspections route must open the Inspections tab.");
assert.match(maintenance, /handledVehicleIntent = useRef\(requestedVehicleId\)/, "The initial vehicle handoff must be recorded so it is consumed only once.");
assert.match(maintenance, /if \(!requestedVehicleId\) \{[\s\S]*?handledVehicleIntent\.current = "";[\s\S]*?return;/, "Removing vehicleId must reset the consumed intent so a later handoff for that vehicle can open again.");
assert.match(maintenance, /if \(handledVehicleIntent\.current !== requestedVehicleId\) \{[\s\S]*?handledVehicleIntent\.current = requestedVehicleId;[\s\S]*?setCreateOpen\(true\);[\s\S]*?\}/, "A new vehicleId while the route is mounted must open the work-order dialog exactly once.");
assert.match(maintenance, /key=\{requestedVehicleId \|\| "manual"\}/, "A changed vehicle handoff must remount the dialog with the requested vehicle selected.");
assert.match(maintenance, /onClose=\{\(\) => \{[\s\S]*?handledVehicleIntent\.current = requestedVehicleId;[\s\S]*?setCreateOpen\(false\);[\s\S]*?next\.delete\("vehicleId"\);[\s\S]*?setSearchParams\(next, \{ replace: true \}\);/, "Closing the dialog must consume and remove the current vehicle handoff without reopening it.");
assert.match(maintenance, /activeTab === "Overview" && insights\.length/, "Maintenance insight banners must not push route-specific queues below the fold.");
assert.match(admin, /location\.pathname === "\/user-management" \? "users"/, "User Management must open the Users workspace.");
assert.match(commandCenterEndpoint, /j\.id job_id, COALESCE\(j\.job_number,j\.job_code\) shipment_number/, "Command Center shipment exceptions must expose their canonical job ID and shipment number.");
assert.match(commandCenterEndpoint, /jobId\s*= r\.GetValueOrDefault\("jobId"\)[\s\S]*?shipmentNumber\s*= r\.GetValueOrDefault\("shipmentNumber"\)/, "Command Center must map persisted shipment identity into its exception response.");
assert.match(commandCenter, /if \(jobId\) return `\$\{route\}\?jobId=\$\{encodeURIComponent\(jobId\)\}`;[\s\S]*?exception\.shipmentNumber/, "Command Center must route shipment exceptions by canonical job ID before using the text fallback.");
assert.match(commandCenter, /navigate\(exceptionActionRoute\(exc\)\)/, "The Command Center exception action must use the identity-preserving route helper.");
assert.match(activeShipmentsApi, /interface ActiveShipmentFilters[\s\S]*?jobId\?: string;/, "The Active Shipments API client must accept a canonical job ID.");
assert.match(activeShipmentsApi, /active-shipments', \{ params: params\(input\), signal \}/, "The Active Shipments list request must send the job ID filter.");
assert.match(activeShipmentsApi, /active-shipments\/export\$\{query/, "The Active Shipments export request must send the same filtered query.");
assert.match(activeShipmentsEndpoint, /if \(!long\.TryParse\(rawJobId,[\s\S]*?exactJobId <= 0\)/, "The Active Shipments backend must reject invalid job IDs.");
assert.match(activeShipmentsEndpoint, /clauses\.Add\("id=@jobId"\)/, "The Active Shipments backend must constrain the projection by exact job ID.");
assert.match(activeShipmentsEndpoint, /command\.Parameters\.AddWithValue\("@jobId", parsedJobId\.Value\)/, "The exact job ID filter must be parameterized.");
assert.match(activeShipments, /searchParams\.get\('jobId'\)\?\.trim\(\)/, "Active Shipments must consume the Command Center job ID handoff.");
assert.match(activeShipments, /jobId: requestedJobId \|\| undefined/, "Active Shipments must pass the requested job ID to its API client.");
assert.match(activeShipments, /setSearchParams\(\(current\) => \{[\s\S]*?next\.delete\('search'\);[\s\S]*?next\.delete\('jobId'\);[\s\S]*?\}, \{ replace: true \}\)/, "Clear filters must consume both cross-module search and job ID context.");
assert.doesNotMatch(activeShipments, /aria-labelledby="active-shipments-title"/, "Active Shipments must not reference a missing heading id.");

assert.match(devices, /Bulk tools/, "Secondary device import and export actions must stay behind one compact control.");
assert.match(devices, /integrations\?provider=/, "Device provider transitions must preserve the provider context.");
assert.match(safety, /integrations\?provider=samsara&intent=camera-intake/, "Camera intake must open the Samsara connector context.");
assert.match(integrations, /searchParams\.get\("provider"\)/, "Integrations must consume provider context from owning modules.");
assert.match(integrations, /setConfigTarget\(target\)/, "A contextual provider setup transition must open the matching connector workflow.");

assert.doesNotMatch(platformUi, /rounded-\[20px\]/, "Platform primitives must not restore the oversized legacy radius.");

console.log("App-wide UI static source contract passed (no rendered route, browser, viewport, interaction, or accessibility claim).");
