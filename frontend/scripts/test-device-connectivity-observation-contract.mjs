import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const service = readFileSync(resolve(root, "src/services/telematicsService.ts"), "utf8");
const page = readFileSync(resolve(root, "src/pages/IotDevicesPage.tsx"), "utf8");

test("provider observation projection fails closed across all external claims", () => {
  assert.match(service, /provider_verified_claim === false/);
  assert.match(service, /physical_connectivity_claim === false/);
  assert.match(service, /certification_claim === false/);
  assert.match(service, /softwareObservationAvailable/);
  assert.match(service, /sourceAuthenticationStatus: softwareObservationAvailable \? "Authenticated" : "Unverified"/);
});

test("customer surface shows reported statuses without claiming live device truth", () => {
  assert.match(page, /Latest carrier\/provider observation/);
  assert.match(page, /Reported subscription/);
  assert.match(page, /Reported network/);
  assert.match(page, /Reported data session/);
  assert.match(page, /Provider-reported software status only/);
  assert.match(page, /does not prove radio attachment, telemetry delivery, physical operation, or certification/);
});

test("missing authentic exact-profile observation remains visibly unavailable", () => {
  assert.match(page, /No exact-profile observation from an authenticated carrier\/provider adapter is available/);
});
