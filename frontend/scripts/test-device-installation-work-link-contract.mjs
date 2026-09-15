import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const service = readFileSync(resolve(root, "src/services/telematicsService.ts"), "utf8");
const page = readFileSync(resolve(root, "src/pages/IotDevicesPage.tsx"), "utf8");

test("installation link projection rejects promoted or malformed server claims", () => {
  assert.match(service, /row\.link_assurance_status !== "RecordedUnverified"/);
  assert.match(service, /row\.link_physical_work_claim !== false/);
  assert.match(service, /row\.link_certification_claim !== false/);
  assert.match(service, /installationEffectiveInstant\(row\.linked_at\) === null/);
  assert.match(service, /LinkedAwaitingIndependentVerification/);
});

test("operator explicitly links only a matching persisted installation", () => {
  assert.match(page, /detail\.currentInstallation/);
  assert.match(page, /String\(installation\.vehicleId \?\? ""\) === workPackage\.vehicleId/);
  assert.match(page, /Link recorded installation/);
  assert.match(page, /linkInstallationWorkPackage/);
});

test("customer surface states the link remains unverified", () => {
  assert.match(page, /Linking records traceability only; physical work and certification remain unverified/);
  assert.match(page, /This link does not verify physical work/);
  assert.match(service, /Physical work and certification remain unverified/);
});
