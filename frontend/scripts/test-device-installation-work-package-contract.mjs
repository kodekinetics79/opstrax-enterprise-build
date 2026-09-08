import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const service = readFileSync(resolve(root, "src/services/telematicsService.ts"), "utf8");
const page = readFileSync(resolve(root, "src/pages/IotDevicesPage.tsx"), "utf8");

test("installation work-package projection rejects all promoted claims", () => {
  assert.match(service, /row\.physical_appointment_claim !== false/);
  assert.match(service, /row\.physical_work_claim !== false/);
  assert.match(service, /row\.certification_claim !== false/);
  assert.match(service, /row\.assurance_status !== "Unverified"/);
  assert.match(service, /row\.content_verification_status !== "Unverified"/);
  assert.match(service, /installationEffectiveInstant\(appointmentStartText\)/);
  assert.match(service, /installationEffectiveInstant\(observedAt\)/);
  assert.match(service, /installationEffectiveInstant\(capturedAt\)/);
  assert.match(service, /objectKey\.includes\("\.\."\)/);
});

test("operator must explicitly record appointment, checklist result, and artifact hash", () => {
  assert.match(page, /Record appointment plan/);
  assert.match(page, /Choose only the result actually observed/);
  assert.match(page, /Record unverified observation/);
  assert.match(page, /Governed-storage object key/);
  assert.match(page, /SHA-256/);
  assert.match(page, /Record unverified reference/);
});

test("customer surface states the exact assurance boundary", () => {
  assert.match(page, /Every result is an operator-recorded assertion/);
  assert.match(page, /Attendance, artifact content, physical work, and certification remain unverified/);
  assert.match(page, /Scheduling does not claim that the appointment occurred/);
  assert.match(page, /Physical attendance", "Unverified/);
  assert.match(page, /Certification", "Not claimed/);
});
