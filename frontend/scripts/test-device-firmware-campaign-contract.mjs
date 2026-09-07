import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const service = readFileSync(resolve(root, "src/services/telematicsService.ts"), "utf8");
const page = readFileSync(resolve(root, "src/pages/IotDevicesPage.tsx"), "utf8");

test("customer firmware workflow is explicitly planning-only", () => {
  assert.match(page, /Firmware campaign planning/);
  assert.match(page, /Planning record only\. OpsTrax does not dispatch an OTA command/);
  assert.match(page, /Record firmware plan/);
  assert.doesNotMatch(page, /Start firmware update/);
});

test("firmware API requires a fail-closed no-upgrade acknowledgement", () => {
  assert.match(service, /normalized\.remote_upgrade_claim !== false/);
  assert.match(service, /row\.remote_upgrade_claim !== false/);
  assert.match(service, /remoteUpgradeClaim: false/);
  assert.doesNotMatch(service, /\/api\/telemetry\/.*(?:dispatch|execute).*firmware/i);
});

test("campaign plans preserve scheduling, batches, rollback, source and idempotency", () => {
  for (const field of [
    "targetFirmwareVersion", "rollbackFirmwareVersion", "rolloutStrategy", "scheduledFor",
    "maintenanceWindowMinutes", "batchSize", "deviceIds", "changeReason", "sourceReference", "idempotencyKey",
  ]) assert.match(service, new RegExp(`${field}:`));
  assert.match(page, /crypto\.randomUUID\(\)/);
  assert.match(page, /Provider capability/);
  assert.match(page, /external hold/i);
});

test("campaign targets use the safe numeric JSON identifier boundary", () => {
  assert.match(service, /input\.deviceIds\.map\(installationBodyId\)/);
  assert.match(service, /deviceIds\.some\(id => id === null\)/);
  assert.doesNotMatch(service, /deviceIds: deviceIds\.map\(Number\)/);
});
