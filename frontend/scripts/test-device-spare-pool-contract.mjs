import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const service = readFileSync(resolve(root, "src/services/telematicsService.ts"), "utf8");
const page = readFileSync(resolve(root, "src/pages/IotDevicesPage.tsx"), "utf8");

test("spare-pool projections fail closed on every promoted evidence claim", () => {
  assert.match(service, /row\.inventory_assurance_status !== "OperatorRecordedUnverified"/);
  assert.match(service, /row\.event_status !== "OperatorRecorded"/);
  assert.match(service, /row\.physical_possession_claim !== false/);
  assert.match(service, /row\.condition_verified_claim !== false/);
  assert.match(service, /row\.compatibility_claim !== false/);
  assert.match(service, /row\.certification_claim !== false/);
  assert.match(service, /payload\.physical_possession_claim !== false/);
  assert.match(service, /payload\.certification_claim !== false/);
});

test("customer controls expose only the governed pool state machine", () => {
  assert.match(page, /Spare-device pool planning/);
  assert.match(page, /Add to pool/);
  assert.match(page, /Reserve for RMA/);
  assert.match(page, /Release reservation/);
  assert.match(page, /Remove from plan/);
  assert.match(page, /do not prove physical possession, device condition, compatibility, installation, or certification/);
});

test("writes preserve exact device and RMA identities with idempotency", () => {
  assert.match(service, /devices\/\$\{canonicalId\}\/spare-pool-actions/);
  assert.match(service, /canonicalDeviceLifecycleId\(input\.rmaCaseId\)/);
  assert.match(service, /idempotentReplay: payload\.idempotent_replay === true/);
  assert.match(service, /poolEvent\.rmaCaseId !== input\.rmaCaseId/);
});
