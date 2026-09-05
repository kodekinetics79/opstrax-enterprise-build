import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const source = async (path) => readFile(new URL(`../${path}`, import.meta.url), "utf8");

test("production store builds fail closed without privacy and support metadata", async () => {
  const config = await source("app.config.ts");
  assert.match(config, /EXPO_PUBLIC_PRIVACY_URL/);
  assert.match(config, /EXPO_PUBLIC_SUPPORT_URL/);
  assert.match(config, /requirePublicHttpsUrl\(PRIVACY_URL/);
  assert.match(config, /requirePublicHttpsUrl\(SUPPORT_URL/);
  assert.match(config, /Production store builds must set EXPO_PUBLIC_PRODUCT/);
});

test("account creation automatically requires a direct deletion resource", async () => {
  const [config, runtime, settings] = await Promise.all([
    source("app.config.ts"),
    source("src/config.ts"),
    source("src/screens/SettingsScreen.tsx"),
  ]);
  assert.match(config, /ACCOUNT_CREATION_ENABLED/);
  assert.match(config, /requirePublicHttpsUrl\(ACCOUNT_DELETION_URL/);
  assert.match(runtime, /ACCOUNT_DELETION_URL/);
  assert.match(settings, /Delete my account/);
  assert.match(settings, /this app does not currently create user accounts/i);
});

test("privacy and support resources are discoverable inside the app", async () => {
  const settings = await source("src/screens/SettingsScreen.tsx");
  assert.match(settings, /Privacy policy/);
  assert.match(settings, /Support/);
  assert.match(settings, /Linking\.openURL/);
});

test("current Expo SDK line is Android API 36 store-capable", async () => {
  const pkg = JSON.parse(await source("package.json"));
  assert.match(pkg.dependencies.expo, /^~56\./);
});
