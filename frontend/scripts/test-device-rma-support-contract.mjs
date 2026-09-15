import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const service = readFileSync(resolve(root, "src/services/telematicsService.ts"), "utf8");
const page = readFileSync(resolve(root, "src/pages/IotDevicesPage.tsx"), "utf8");

test("RMA support projections fail closed on every promoted outcome", () => {
  assert.match(service, /row\.support_action_status !== "OperatorRecorded"/);
  assert.match(service, /row\.support_response_claim !== false/);
  assert.match(service, /row\.physical_outcome_claim !== false/);
  assert.match(service, /row\.warranty_acceptance_claim !== false/);
  assert.match(service, /payload\.support_response_claim !== false/);
  assert.match(service, /payload\.warranty_acceptance_claim !== false/);
});

test("support actions expose accountable routing and explicit evidence limits", () => {
  assert.match(page, /Current owner/);
  assert.match(page, /Support queue/);
  assert.match(page, /Take ownership/);
  assert.match(page, /Escalate support case/);
  assert.match(page, /does not claim a support response, warranty acceptance, or physical outcome/);
});

test("support writes preserve exact case identity and idempotency", () => {
  assert.match(service, /rma-cases\/\$\{canonicalId\}\/support-actions/);
  assert.match(service, /canonicalDeviceLifecycleId\(caseId\)/);
  assert.match(service, /idempotentReplay: payload\.idempotent_replay === true/);
  assert.match(service, /supportAction\.supportQueue !== input\.supportQueue\.trim\(\)/);
});
