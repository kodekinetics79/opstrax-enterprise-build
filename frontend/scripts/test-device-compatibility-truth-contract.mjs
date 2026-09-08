import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const service = readFileSync(resolve(root, "src/services/telematicsService.ts"), "utf8");
const page = readFileSync(resolve(root, "src/pages/IotDevicesPage.tsx"), "utf8");

test("client compatibility projection stays fail-closed regardless of API vocabulary", () => {
  assert.match(service, /certificationStatus:\s*"ExternalHold"/);
  assert.match(service, /maximumTier:\s*"Unverified"/);
  assert.match(service, /catalogSupportTier:\s*"Unverified"/);
  assert.match(service, /certificationReference:\s*null/);
  assert.match(service, /certificationDate:\s*null/);
  assert.match(service, /physicalEvidenceClaim:\s*false/);
  assert.match(service, /providerEvidenceClaim:\s*false/);
  assert.match(service, /certificationClaim:\s*false/);
  assert.doesNotMatch(service, /certificationStatus:\s*String\(compatibilityRow/);
  assert.doesNotMatch(service, /maximumTier:\s*String\(compatibilityRow/);
});

test("candidate SHA is exposed only when it is exact lowercase forty-character identity", () => {
  assert.match(service, /\^\[0-9a-f\]\{40\}\$/);
  assert.match(service, /candidateSha:\s*typeof compatibilityRow\.candidate_sha/);
});

test("device onboarding records exact tuple inputs without making a certification claim", () => {
  for (const field of ["manufacturer", "hardwareRevision", "firmwareVersion"])
    assert.match(page, new RegExp(`${field}: payload\\.${field}\\.trim\\(\\)`));
  assert.match(page, /Hardware identity fields are operator-recorded candidate data/);
  assert.match(page, /They do not prove physical identity, compatibility, or certification/);
});

test("large-fleet import preserves the same exact hardware tuple fields", () => {
  assert.match(page, /columns:\s*\["deviceSerial", "branchCode", "imei", "deviceCategory", "manufacturer", "deviceModel", "hardwareRevision", "provider", "firmwareVersion", "notes"\]/);
});

test("device details show the external hold and every required physical evidence family", () => {
  assert.match(page, /Hardware compatibility truth/);
  assert.match(page, /Certification status/);
  assert.match(page, />External hold</);
  assert.match(page, /Physical bench, route, recovery, soak, security, provider, and independent acceptance evidence is still required/);
  assert.match(page, /Registration, installation, commissioning, or live data never certifies hardware/);
});

test("capability catalog is visible but remains explicitly engineering-declared and unverified", () => {
  for (const label of [
    "Capability declaration", "Protocols", "Supported fields", "Supported events",
    "Supported commands", "Known limitations", "Catalog support tier",
    "Certification reference", "Certification date",
  ]) assert.match(page, new RegExp(label));
  assert.match(page, /Engineering-declared \/ unverified/);
  assert.match(page, /Engineering-declared capabilities describe intended software behavior only/);
  assert.match(service, /capabilityDeclarationStatus === "EngineeringDeclaredUnverified"/);
});
