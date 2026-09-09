import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const service = readFileSync(resolve(root, "src/services/telematicsService.ts"), "utf8");
const page = readFileSync(resolve(root, "src/pages/IotDevicesPage.tsx"), "utf8");
const rbac = readFileSync(resolve(root, "src/auth/rbacConfig.ts"), "utf8");
const policy = readFileSync(resolve(root, "../backend-dotnet/Services/DeviceRemoteCommandPolicy.cs"), "utf8");

test("customer command surface distinguishes request admission from delivery and outcome", () => {
  assert.match(page, /Capability-governed remote commands/);
  assert.match(page, /does not prove provider dispatch, device acknowledgement, application, or any physical outcome/);
  assert.match(page, /Record command request/);
  assert.match(page, /External hold/);
  assert.doesNotMatch(page, />Execute command</);
});

test("remote command catalog is bounded and excludes immobilization controls", () => {
  for (const command of ["RequestPosition", "RequestDiagnostics", "RestartDevice"])
    assert.match(policy, new RegExp(`\\["${command}"\\]`));
  assert.doesNotMatch(policy, /(?:Immobilize|LockVehicle|UnlockVehicle)/);
  assert.match(policy, /SHA256\.HashData/);
  assert.match(page, /Safety confirmation/);
});

test("API projections fail closed on certification delivery and physical claims", () => {
  assert.match(service, /row\.certification_claim !== false/);
  assert.match(service, /row\.provider_delivery_claim !== false \|\| row\.physical_outcome_claim !== false/);
  assert.match(service, /hasRemoteCommandGovernance && \(remoteCommandGovernance\.provider_delivery_claim !== false/);
  assert.match(service, /payload\.request_recorded !== true/);
  assert.match(service, /payload\.dispatched !== false/);
  assert.match(service, /payload\.acknowledged !== false/);
  assert.match(service, /payload\.applied !== false/);
  assert.match(service, /\/api\/telemetry\/devices\/\$\{canonicalId\}\/commands/);
});

test("command submission has dedicated permission, idempotency and no automatic retry", () => {
  assert.match(rbac, /TELEMATICS_DEVICES_COMMAND: "telematics:devices:command"/);
  assert.match(page, /retry: false/);
  assert.match(page, /remoteCommandSubmitting/);
  assert.match(service, /idempotencyKey: input\.idempotencyKey/);
});
