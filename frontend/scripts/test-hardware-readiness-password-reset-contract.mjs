import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const hardware = readFileSync(resolve(root, "src/pages/platform/PlatformHardwareReadinessPage.tsx"), "utf8");
const platformApi = readFileSync(resolve(root, "src/services/platformApi.ts"), "utf8");
const platformApp = readFileSync(resolve(root, "src/pages/platform/PlatformApp.tsx"), "utf8");
const platformShell = readFileSync(resolve(root, "src/layouts/PlatformShell.tsx"), "utf8");
const admin = readFileSync(resolve(root, "src/pages/AdminPage.tsx"), "utf8");
const adminApi = readFileSync(resolve(root, "src/services/adminApi.ts"), "utf8");

test("platform hardware readiness is visible and permission gated", () => {
  assert.match(platformApp, /path="hardware-readiness"/);
  assert.match(platformApp, /permission="platform:devices:view"/);
  assert.match(platformShell, /Hardware Readiness/);
  assert.match(hardware, /Register device candidate/);
  assert.match(platformApi, /device-compatibility-candidates/);
});

test("candidate intake preserves exact identity and fail-closed certification truth", () => {
  for (const field of ["manufacturer", "deviceModel", "hardwareRevision", "firmwareVersion"])
    assert.match(hardware, new RegExp(field));
  assert.match(hardware, /Physical evidence cannot be entered here/);
  assert.match(hardware, /it cannot certify hardware/i);
  assert.match(hardware, /External Hold/);
  assert.match(hardware, /Await physical evidence/);
});

test("tenant administrator can set a password without email", () => {
  assert.match(admin, /hasPermission\(PERMISSIONS\.USERS_MANAGE\)/);
  assert.match(admin, /Set new password/);
  assert.match(admin, /no email will be sent/);
  assert.match(admin, /revokes every active session and invalidates outstanding reset links/);
  assert.match(adminApi, /\/api\/admin\/users\/\$\{id\}\/reset-password/);
});
