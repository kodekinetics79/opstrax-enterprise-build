import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { loadSecureEnvFile, PROFILE_LIMITS, resolveLoadConfig, sanitizedConfig } from "./load_guard.mjs";

const directory = path.dirname(fileURLToPath(import.meta.url));
const base = {
  LOAD_TARGET_ENV: "staging",
  LOAD_API_BASE_URL: "https://api-staging.example.test",
  LOAD_STAGING_HOSTS: "api-staging.example.test",
  LOAD_ISOLATED_STAGING_ACK: "I_UNDERSTAND_THIS_IS_AN_ISOLATED_STAGING_TENANT",
  LOAD_BEARER_TOKEN: "unit-test-token",
};

test("known production UI and API hosts are refused", () => {
  for (const hostname of ["opstrax.vercel.app", "osptrax-fleet-management.onrender.com"]) {
    assert.throws(() => resolveLoadConfig({ ...base, LOAD_API_BASE_URL: `https://${hostname}`, LOAD_STAGING_HOSTS: hostname }), /production host/);
  }
});

test("target must be HTTPS isolated staging with exact host membership", () => {
  assert.throws(() => resolveLoadConfig({ ...base, LOAD_TARGET_ENV: "production" }), /exactly staging/);
  assert.throws(() => resolveLoadConfig({ ...base, LOAD_API_BASE_URL: "http://api-staging.example.test" }), /use HTTPS/);
  assert.throws(() => resolveLoadConfig({ ...base, LOAD_STAGING_HOSTS: "other.example.test" }), /explicitly listed/);
  assert.throws(() => resolveLoadConfig({ ...base, LOAD_ISOLATED_STAGING_ACK: "yes" }), /acknowledgement/);
});

test("credential is mandatory but omitted from sanitized output", () => {
  assert.throws(() => resolveLoadConfig({ ...base, LOAD_BEARER_TOKEN: "" }), /BEARER_TOKEN/);
  const config = resolveLoadConfig(base);
  assert.equal(JSON.stringify(sanitizedConfig(config)).includes("unit-test-token"), false);
});

test("default smoke profile is bounded", () => {
  const config = resolveLoadConfig(base);
  assert.equal(config.profile, "smoke");
  assert.equal(config.iterationsPerSecond, 1);
  assert.equal(config.maximumRequestsPerSecond, 2);
  assert.equal(config.durationSeconds, 30);
  assert.equal(config.maxVus, 4);
});

test("load and stress profiles use declared caps", () => {
  for (const [profile, limits] of Object.entries(PROFILE_LIMITS)) {
    const config = resolveLoadConfig({ ...base, LOAD_PROFILE: profile });
    assert.equal(config.iterationsPerSecond, limits.iterationsPerSecond);
    assert.equal(config.durationSeconds, limits.durationSeconds);
    assert.equal(config.maxVus, limits.maxVus);
  }
});

test("profile values may be lowered but never raised", () => {
  const lowered = resolveLoadConfig({
    ...base,
    LOAD_PROFILE: "stress",
    LOAD_ITERATIONS_PER_SECOND: "2",
    LOAD_DURATION_SECONDS: "60",
    LOAD_MAX_VUS: "5",
  });
  assert.equal(lowered.maximumRequestsPerSecond, 4);
  assert.throws(() => resolveLoadConfig({ ...base, LOAD_PROFILE: "stress", LOAD_MAX_VUS: "51" }), /safety cap/);
  assert.throws(() => resolveLoadConfig({ ...base, LOAD_PROFILE: "load", LOAD_DURATION_SECONDS: "301" }), /safety cap/);
  assert.throws(() => resolveLoadConfig({ ...base, LOAD_PROFILE: "smoke", LOAD_ITERATIONS_PER_SECOND: "2" }), /safety cap/);
});

test("only safe same-origin GET paths are accepted", () => {
  for (const invalid of ["https://evil.example/x", "//evil.example/x", "/../admin", "/api/x?delete=true", "/api/x#fragment"]) {
    assert.throws(() => resolveLoadConfig({ ...base, LOAD_AUTHENTICATED_PATH: invalid }), /same-origin/);
  }
});

test("k6 workload contains GET only and bounded response handling", () => {
  const source = fs.readFileSync(path.join(directory, "readonly.js"), "utf8");
  assert.equal((source.match(/http\.get\(/g) || []).length, 2);
  assert.doesNotMatch(source, /http\.(post|put|patch|del|delete)\s*\(/i);
  assert.match(source, /discardResponseBodies:\s*true/);
  assert.match(source, /redirects:\s*0/g);
  assert.match(source, /maxVUs:\s*maxVus/);
  assert.doesNotMatch(source, /^\s*maxVUs,\s*$/m);
  assert.match(source, /dropped_iterations:\s*\["count==0"\]/);
  assert.match(source, /checks:\s*\["rate==1"\]/);
  assert.match(source, /http_req_failed:\s*\["rate<0\.005"\]/);
  assert.match(source, /http_req_duration:\s*\["p\(95\)<500",\s*"p\(99\)<5000"\]/);
  assert.match(source, /"http_req_duration\{surface:public-health\}"/);
  assert.match(source, /"http_req_duration\{surface:authenticated-read\}"/);
  assert.match(source, /"http_req_failed\{surface:public-health\}"/);
  assert.match(source, /"http_req_failed\{surface:authenticated-read\}"/);
  assert.doesNotMatch(source, /p\(95\)<2000/);
  assert.doesNotMatch(source, /rate<0\.01/);
});

test("runner can retain a structured k6 summary without exposing it in dry-run output", () => {
  const source = fs.readFileSync(path.join(directory, "run_load.mjs"), "utf8");
  assert.match(source, /LOAD_SUMMARY_EXPORT_PATH/);
  assert.match(source, /--summary-export/);
  assert.doesNotMatch(JSON.stringify(sanitizedConfig(resolveLoadConfig({ ...base, LOAD_SUMMARY_EXPORT_PATH: "secret-path" }))), /secret-path/);
});

test("tracked credential template is blank", () => {
  const template = fs.readFileSync(path.join(directory, ".env.local.example"), "utf8");
  assert.match(template, /^LOAD_BEARER_TOKEN=$/m);
  assert.doesNotMatch(template, /Bearer\s+\S+|eyJ[A-Za-z0-9_-]+\./);
});

test("runtime env file must be mode 0600", () => {
  const temporary = fs.mkdtempSync(path.join(os.tmpdir(), "opstrax-load-env-"));
  const envPath = path.join(temporary, ".env.local");
  try {
    fs.writeFileSync(envPath, "LOAD_TARGET_ENV=staging\n", { mode: 0o644 });
    assert.throws(() => loadSecureEnvFile(envPath, {}), /mode 0600/);
    fs.chmodSync(envPath, 0o600);
    assert.equal(loadSecureEnvFile(envPath, {}).LOAD_TARGET_ENV, "staging");
  } finally {
    fs.rmSync(temporary, { recursive: true });
  }
});

test("staging workflow uses protected auth state without logging bearer credentials", () => {
  const workflow = fs.readFileSync(path.resolve(directory, "../../.github/workflows/staging-load-certification.yml"), "utf8");
  assert.match(workflow, /environment:\s*Staging/);
  assert.match(workflow, /secrets\.E2E_TENANT_AUTH_STATE_B64/);
  assert.match(workflow, /opstrax\.session\.v3/);
  assert.match(workflow, /CERT-LARGE-20260825/);
  assert.match(workflow, /EXPECTED_VEHICLE_COUNT:\s*'1001'/);
  assert.match(workflow, /Post-load exact SHA and readiness\n\s+if:\s*\$\{\{ always\(\) \}\}/);
  assert.match(workflow, /grafana\/setup-k6-action@db07bd9765aac508ef18982e52ab937fe633a065/);
  assert.match(workflow, /k6-version:\s*'2\.2\.0'/);
  assert.match(workflow, /node tests\/load\/run_load\.mjs --execute/);
  assert.match(workflow, /opstrax-staging-api\.onrender\.com/);
  assert.doesNotMatch(workflow, /echo\s+["']?\$LOAD_BEARER_TOKEN|printf[^\n]*LOAD_BEARER_TOKEN/);
});
