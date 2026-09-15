import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const service = readFileSync(resolve(root, "src/services/telematicsService.ts"), "utf8");
const page = readFileSync(resolve(root, "src/pages/IotDevicesPage.tsx"), "utf8");
const rbac = readFileSync(resolve(root, "src/auth/rbacConfig.ts"), "utf8");

test("RMA customer workflow exposes real support records and explicit evidence limits", () => {
  assert.match(page, /RMA, custody & replacement/);
  assert.match(page, /Open RMA case/);
  assert.match(page, /Add custody event/);
  assert.match(page, /Plan replacement/);
  assert.match(page, /does not verify physical receipt, vendor warranty acceptance, replacement installation, or device readiness/);
});

test("RMA API projections fail closed across warranty custody and replacement claims", () => {
  assert.match(service, /row\.physical_evidence_claim !== false \|\| row\.warranty_evidence_status !== "Unverified"/);
  assert.match(service, /row\.physical_completion_claim !== false \|\| row\.evidence_status !== "Unverified"/);
  assert.match(service, /row\.physical_swap_claim !== false \|\| row\.physical_swap_status !== "ExternalHold"/);
  assert.match(service, /payload\.physical_evidence_claim !== false/);
  assert.match(service, /payload\.physical_completion_claim !== false/);
  assert.match(service, /payload\.physical_swap_claim !== false/);
});

test("RMA mutations have dedicated permission and idempotent source facts", () => {
  assert.match(rbac, /TELEMATICS_DEVICES_RMA: "telematics:devices:rma"/);
  assert.match(page, /canManageRma/);
  for (const field of ["supportSlaReference", "responseDueAt", "sourceReference", "evidenceReference", "idempotencyKey"])
    assert.match(service, new RegExp(field));
  assert.match(service, /canonicalDeviceLifecycleId\(caseId\)/);
});
