import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const service = readFileSync(resolve(root, "src/services/telematicsService.ts"), "utf8");
const page = readFileSync(resolve(root, "src/pages/IotDevicesPage.tsx"), "utf8");

test("customer surface identifies SIM data as inventory rather than connectivity", () => {
  assert.match(page, /SIM \/ eSIM inventory/);
  assert.match(page, /Inventory assignment only\. Network attachment and live telemetry require separate observed evidence/);
  assert.match(service, /connectivity_claim !== false/);
  assert.match(service, /: "Unknown"/);
  assert.doesNotMatch(service, /profileKind: row\.profile_kind === "eSIM" \? "eSIM" : "PhysicalSIM"/);
  assert.doesNotMatch(service, /assignmentStatus: row\.assignment_status === "Ended" \? "Ended" : "Assigned"/);
});

test("sensitive profile values are submitted but only masked status fields are mapped", () => {
  for (const field of ["iccidLast4", "msisdnLast4", "apnConfigured"])
    assert.match(service, new RegExp(`${field}:`));
  assert.doesNotMatch(service, /iccidEncrypted:/);
  assert.doesNotMatch(service, /msisdnEncrypted:/);
  assert.doesNotMatch(service, /apnEncrypted:/);
  assert.match(page, /ICCID, MSISDN and APN are encrypted/);
});

test("profile changes carry source, effective time, reason, and idempotency", () => {
  for (const field of ["effectiveAt", "changeReason", "sourceReference", "idempotencyKey"])
    assert.match(service, new RegExp(`${field}: string`));
  assert.match(page, /crypto\.randomUUID\(\)/);
  assert.match(page, /Record inventory change/);
});
