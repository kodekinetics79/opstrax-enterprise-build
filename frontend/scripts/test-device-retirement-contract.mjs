import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const service = readFileSync(resolve(root, "src/services/telematicsService.ts"), "utf8");
const page = readFileSync(resolve(root, "src/pages/IotDevicesPage.tsx"), "utf8");

test("retirement response fails closed on promoted physical or certification claims", () => {
  assert.match(service, /payload\.physical_disposition_claim !== false/);
  assert.match(service, /payload\.certification_claim !== false/);
  assert.match(service, /row\.physical_disposition_status !== "Unverified"/);
  assert.match(service, /row\.credentials_revoked !== true/);
  assert.match(service, /rowVersionAfter: after/);
});

test("retirement requires exact device revision and typed serial confirmation", () => {
  assert.match(page, /expectedRowVersion: device\.rowVersion/);
  assert.match(page, /Type RETIRE \$\{retirementTarget\.serialNumber\} to confirm/);
  assert.match(page, /target\.currentInstallationId \|\| target\.assignedVehicleId/);
  assert.match(page, /Record physical removal from the current installation before retirement/);
});

test("customer surface distinguishes software retirement from physical disposition", () => {
  assert.match(page, /The disposition selection is a plan only/);
  assert.match(page, /Physical disposition/);
  assert.match(page, /Device Lifecycle History/);
  assert.doesNotMatch(page, /PanelSection title="Assignment History"/);
});
