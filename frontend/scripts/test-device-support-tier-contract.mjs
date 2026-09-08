import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const service = readFileSync(resolve(root, "src/services/telematicsService.ts"), "utf8");
const page = readFileSync(resolve(root, "src/pages/IotDevicesPage.tsx"), "utf8");

test("support-tier projections fail closed on every promoted assurance claim", () => {
  assert.match(service, /row\.record_status !== "OperatorRecordedUnverified"/);
  assert.match(service, /row\.commercial_entitlement_verified_claim !== false/);
  assert.match(service, /row\.provider_support_claim !== false/);
  assert.match(service, /row\.hardware_supportability_claim !== false/);
  assert.match(service, /row\.certification_claim !== false/);
  assert.match(service, /payload\.commercial_entitlement_verified_claim !== false/);
});

test("customer controls expose assign change and end with the evidence limit", () => {
  assert.match(page, /Device support tier/);
  assert.match(page, /Assign tier/);
  assert.match(page, /Change tier/);
  assert.match(page, /End coverage/);
  assert.match(page, /does not verify a commercial entitlement, provider support, hardware supportability, or certification/);
});

test("writes retain exact device and routing plan facts", () => {
  assert.match(service, /devices\/\$\{canonicalId\}\/support-tier-actions/);
  assert.match(service, /supportTierEvent\.deviceId !== canonicalId/);
  assert.match(service, /supportTierEvent\.routingResponseTargetMinutes !== input\.routingResponseTargetMinutes/);
  assert.match(service, /idempotentReplay: payload\.idempotent_replay === true/);
});
