import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

const read = (relative) => readFileSync(fileURLToPath(new URL(relative, import.meta.url)), "utf8");
const service = read("../src/services/telematicsService.ts");
const page = read("../src/pages/TelematicsCommandPage.tsx");
const maintenancePage = read("../src/pages/MaintenanceCommandPage.tsx");
const maintenanceApi = read("../src/services/maintenanceApi.ts");

assert.match(service, /position_engine_speed_rpm/, "canonical engine speed must reach the browser model");
assert.match(service, /position_engine_hours/, "canonical engine hours must reach the browser model");
assert.match(service, /position_signal_availability/, "signal availability must remain visible");
assert.match(service, /position_signal_evidence_headers/, "capture references must remain visible");
assert.match(service, /Operational observation only — not certification/, "observations must not be labeled certification");
assert.match(service, /signalCaptureReferences/, "capture references must be parsed from persisted evidence headers");
assert.match(page, /"engineSpeed", "engineHours", "batteryVoltage", "signalAvailability"/, "diagnostics table must show decoded J1939 fields");
assert.match(page, /\["Evidence boundary", row\.certificationBoundary\]/, "detail must state the certification boundary");
assert.match(page, /\["Signal capture reference", row\.signalEvidenceReference\]/, "detail must identify retained signal capture evidence");
assert.match(page, /\["Diagnostic capture reference", row\.diagnosticEvidenceReference\]/, "detail must identify retained diagnostic capture evidence");
assert.match(page, /\["Diagnostic capture-set digest", row\.diagnosticEvidenceDigest\]/, "detail must identify the bounded diagnostic capture set");
assert.match(maintenancePage, /diagnosticEvidenceReferenceDigest/, "maintenance fault rows must expose the canonical capture-set digest");
assert.match(page, /\["Safety action", row\.diagnosticSafetyAction\]/, "diagnostics detail must separate evidence from a persisted safety action");
assert.match(maintenancePage, /"Diagnostic Holds"/, "persisted diagnostic holds must have a customer-visible maintenance tab");
assert.match(maintenancePage, /Resolve with evidence/, "a hold must expose its governed resolution workflow");
assert.match(maintenancePage, /Vehicle availability is unchanged until verified resolution/, "acknowledgement must not be presented as release");
assert.match(maintenanceApi, /diagnosticHolds:/, "the customer UI must load the persisted hold ledger");
assert.match(maintenanceApi, /acknowledgeDiagnosticHold:/, "the customer UI must expose hold acknowledgement");
assert.match(maintenanceApi, /resolveDiagnosticHold:/, "the customer UI must submit typed resolution evidence");

console.log("J1939 customer signal visibility contract passed.");
