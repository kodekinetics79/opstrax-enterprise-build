import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

const read = (relative) => readFileSync(fileURLToPath(new URL(relative, import.meta.url)), "utf8");
const service = read("../src/services/telematicsService.ts");
const page = read("../src/pages/TelematicsCommandPage.tsx");

assert.match(service, /position_engine_speed_rpm/, "canonical engine speed must reach the browser model");
assert.match(service, /position_engine_hours/, "canonical engine hours must reach the browser model");
assert.match(service, /position_signal_availability/, "signal availability must remain visible");
assert.match(service, /position_signal_evidence_headers/, "capture references must remain visible");
assert.match(service, /Operational observation only — not certification/, "observations must not be labeled certification");
assert.match(service, /signalCaptureReferences/, "capture references must be parsed from persisted evidence headers");
assert.match(page, /"engineSpeed", "engineHours", "batteryVoltage", "signalAvailability"/, "diagnostics table must show decoded J1939 fields");
assert.match(page, /\["Evidence boundary", row\.certificationBoundary\]/, "detail must state the certification boundary");
assert.match(page, /\["Capture reference", row\.signalEvidenceReference\]/, "detail must identify retained capture evidence");

console.log("J1939 customer signal visibility contract passed.");
