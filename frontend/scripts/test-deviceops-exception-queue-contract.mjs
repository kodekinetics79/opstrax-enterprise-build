import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const service = readFileSync(resolve(root, "src/services/telematicsService.ts"), "utf8");
const page = readFileSync(resolve(root, "src/pages/IotDevicesPage.tsx"), "utf8");

test("device operations queue lists only persisted software gaps", () => {
  for (const reason of [
    "Complete exact hardware identity",
    "Record current installation",
    "Record SIM/eSIM profile",
    "Restore current telemetry observation",
    "Resolve device lifecycle hold",
    "Resolve open RMA",
  ]) assert.ok(service.includes(reason));
  assert.match(page, /Software Gaps/);
  assert.match(page, /Operations gaps/);
});

test("missing assessment fields cannot become a healthy result", () => {
  assert.match(service, /deviceOpsAssessmentAvailable/);
  assert.match(service, /projectedDeviceOpsGapCount === derivedDeviceOpsGaps\.length/);
  assert.match(service, /Assessment unavailable/);
  assert.match(service, /readinessGaps:[\s\S]*\? null/);
  assert.match(page, /readinessGapCount == null \? "Pending"/);
  assert.match(page, /readinessGapCount \? "Watch" : "Healthy"/);
});

test("software queue never becomes certification evidence", () => {
  assert.match(page, /Neither measure is certification evidence/);
  assert.match(page, /Hardware\/provider certification holds tracked separately/);
  assert.match(page, /Hardware, provider, and certification holds remain separate/);
});
