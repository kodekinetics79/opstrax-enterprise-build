#!/usr/bin/env node
import assert from "node:assert/strict";
import { cp, mkdtemp, readFile, readdir, rm, symlink } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const mobileRoot = new URL("../", import.meta.url);
const mobilePath = fileURLToPath(mobileRoot);
const expoBin = join(mobilePath, "node_modules", ".bin", "expo");

function runExpo(args, cwd = mobilePath) {
  const result = spawnSync(expoBin, args, {
    cwd,
    encoding: "utf8",
    env: { ...process.env, EXPO_NO_TELEMETRY: "1" },
  });
  if (result.status !== 0) {
    throw new Error(`Expo ${args.join(" ")} failed:\n${result.stdout}\n${result.stderr}`);
  }
  return result.stdout;
}

const resolved = JSON.parse(runExpo(["config", "--type", "public", "--json"]));
assert.equal(resolved.scheme, undefined, "Resolved Expo config must not expose a custom URL scheme");
assert.equal(resolved.android?.intentFilters, undefined, "Resolved Expo config must not expose Android intent filters");
assert.equal(resolved.ios?.associatedDomains, undefined, "Resolved Expo config must not expose iOS associated domains");
assert.equal(resolved.ios?.infoPlist?.CFBundleURLTypes, undefined, "Resolved Expo config must not expose iOS URL types");

const probePath = await mkdtemp(join(tmpdir(), "opstrax-native-linking-probe-"));
try {
  for (const file of ["app.config.ts", "index.ts", "package.json", "package-lock.json", "tsconfig.json"]) {
    await cp(join(mobilePath, file), join(probePath, file));
  }
  for (const directory of ["assets", "plugins"]) {
    await cp(join(mobilePath, directory), join(probePath, directory), { recursive: true });
  }
  await symlink(join(mobilePath, "node_modules"), join(probePath, "node_modules"), "dir");

  runExpo(["prebuild", probePath, "--no-install", "--clean", "--platform", "all"], mobilePath);

  const androidManifest = await readFile(join(probePath, "android", "app", "src", "main", "AndroidManifest.xml"), "utf8");
  const inboundAndroidSurface = androidManifest.replace(/<queries>[\s\S]*?<\/queries>/g, "");
  assert.doesNotMatch(inboundAndroidSurface, /android\.intent\.action\.VIEW/, "Generated Android components must not accept VIEW intents");
  assert.doesNotMatch(inboundAndroidSurface, /android\.intent\.category\.BROWSABLE/, "Generated Android components must not be browsable");
  assert.doesNotMatch(inboundAndroidSurface, /<data\b[^>]*android:scheme=/, "Generated Android components must not register a URL scheme");

  const iosDirectory = join(probePath, "ios");
  const iosEntries = await readdir(iosDirectory, { withFileTypes: true });
  const iosApp = iosEntries.find((entry) => entry.isDirectory() && !entry.name.endsWith(".xcodeproj") && !entry.name.endsWith(".xcworkspace"));
  assert.ok(iosApp, "Generated iOS application directory was not found");
  const infoPlist = await readFile(join(iosDirectory, iosApp.name, "Info.plist"), "utf8");
  assert.doesNotMatch(infoPlist, /<key>CFBundleURLTypes<\/key>/, "Generated iOS app must not register URL types");
  assert.doesNotMatch(infoPlist, /<key>CFBundleURLSchemes<\/key>/, "Generated iOS app must not register URL schemes");

  const entitlements = await readFile(join(iosDirectory, iosApp.name, `${iosApp.name}.entitlements`), "utf8");
  assert.doesNotMatch(entitlements, /com\.apple\.developer\.associated-domains|applinks:/, "Generated iOS app must not register universal links");
} finally {
  await rm(probePath, { recursive: true, force: true });
}

process.stdout.write("Resolved Expo config and generated Android/iOS projects expose no inbound URL-to-navigation registration.\n");
