import assert from "node:assert/strict";
import test from "node:test";
import { assertRuntimeSignalsHealthy } from "../lib/signals.mjs";
import { assertRequestAllowed, authStateFor, mutationGate, resolveTarget } from "../lib/target.mjs";

const base = {
  E2E_TARGET_ENV: "staging",
  E2E_UI_BASE_URL: "https://staging.example.test",
  E2E_API_BASE_URL: "https://api-staging.example.test",
  E2E_PRODUCTION_HOSTS: "opstrax.vercel.app",
  E2E_STAGING_HOSTS: "staging.example.test,api-staging.example.test",
};

test("known production host is detected and remains anonymous", () => {
  const target = resolveTarget({
    E2E_TARGET_ENV: "production",
    E2E_UI_BASE_URL: "https://opstrax.vercel.app",
    E2E_API_BASE_URL: "https://api.example.test",
  });
  assert.equal(target.isProduction, true);
  assert.equal(target.environment, "production");
});

test("unknown external target defaults fail-closed to production", () => {
  const target = resolveTarget({ E2E_UI_BASE_URL: "https://unknown.example.test" });
  assert.equal(target.isProduction, true);
});

test("known production host cannot be mislabeled as staging", () => {
  assert.throws(
    () => resolveTarget({ ...base, E2E_UI_BASE_URL: "https://opstrax.vercel.app" }),
    /known production host/i,
  );
});

test("known production API cannot hide behind a staging UI declaration", () => {
  assert.throws(
    () => resolveTarget({
      ...base,
      E2E_API_BASE_URL: "https://osptrax-fleet-management.onrender.com",
      E2E_PRODUCTION_HOSTS: undefined,
    }),
    /known production host/i,
  );
});

test("production rejects every role storage-state channel", () => {
  for (const key of ["E2E_TENANT_AUTH_STATE", "E2E_DRIVER_AUTH_STATE", "E2E_CUSTOMER_AUTH_STATE", "E2E_PLATFORM_AUTH_STATE"]) {
    assert.throws(
      () => resolveTarget({
        E2E_TARGET_ENV: "production",
        E2E_UI_BASE_URL: "https://opstrax.vercel.app",
        [key]: "playwright/.auth/persona.json",
      }),
      /forbidden on production/i,
    );
  }
});

test("production rejects mutation mode", () => {
  assert.throws(
    () => resolveTarget({
      E2E_TARGET_ENV: "production",
      E2E_UI_BASE_URL: "https://opstrax.vercel.app",
      E2E_ALLOW_STAGING_MUTATIONS: "true",
    }),
    /forbidden on production/i,
  );
});

test("production browser guard allows GET and blocks every other method", () => {
  const target = resolveTarget({ E2E_TARGET_ENV: "production", E2E_UI_BASE_URL: "https://opstrax.vercel.app" });
  assert.doesNotThrow(() => assertRequestAllowed(target, "GET"));
  for (const method of ["POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"]) {
    assert.throws(() => assertRequestAllowed(target, method), new RegExp(`blocked ${method}`));
  }
});

test("staging requires HTTPS and local accepts only loopback", () => {
  assert.throws(() => resolveTarget({ ...base, E2E_UI_BASE_URL: "http://staging.example.test" }), /must use HTTPS/);
  assert.throws(
    () => resolveTarget({ E2E_TARGET_ENV: "local", E2E_UI_BASE_URL: "https://dev.example.test" }),
    /only accepts loopback/i,
  );
  assert.equal(resolveTarget({ E2E_TARGET_ENV: "local", E2E_UI_BASE_URL: "http://127.0.0.1:4173" }).environment, "local");
});

test("mutation gate requires every isolated-staging acknowledgement", () => {
  const target = resolveTarget(base);
  assert.equal(mutationGate(target, base).enabled, false);
  const ready = mutationGate(target, {
    ...base,
    E2E_ALLOW_STAGING_MUTATIONS: "true",
    E2E_DISPOSABLE_TENANT_ACK: "I_UNDERSTAND_THIS_WRITES_TEST_DATA",
    E2E_TENANT_AUTH_STATE: "playwright/.auth/tenant.json",
    E2E_CANARY_VEHICLE_ID: "42",
  });
  assert.deepEqual(ready, { enabled: true, reasons: [] });
});

test("mutation gate requires explicit membership for both staging hosts", () => {
  const target = resolveTarget(base);
  const gated = mutationGate(target, {
    ...base,
    E2E_STAGING_HOSTS: "staging.example.test",
    E2E_ALLOW_STAGING_MUTATIONS: "true",
    E2E_DISPOSABLE_TENANT_ACK: "I_UNDERSTAND_THIS_WRITES_TEST_DATA",
    E2E_TENANT_AUTH_STATE: "playwright/.auth/tenant.json",
    E2E_CANARY_VEHICLE_ID: "42",
  });
  assert.equal(gated.enabled, false);
  assert.match(gated.reasons.join(" "), /not both in E2E_STAGING_HOSTS/);
});

test("auth-state paths are absent unless the configured file exists", () => {
  assert.equal(authStateFor("tenant", 0, {}), undefined);
  assert.equal(authStateFor("tenant", 0, { E2E_TENANT_AUTH_STATE: "/definitely/missing/{worker}.json" }), undefined);
});

test("runtime exceptions and 5xx responses fail every browser target", () => {
  assert.doesNotThrow(() => assertRuntimeSignalsHealthy({ pageErrors: [], serverErrors: [] }));
  assert.throws(
    () => assertRuntimeSignalsHealthy({ pageErrors: ["render exploded"], serverErrors: [] }),
    /runtime errors/,
  );
  assert.throws(
    () => assertRuntimeSignalsHealthy({ pageErrors: [], serverErrors: [{ status: 503, url: "/api" }] }),
    /HTTP 5xx/,
  );
});
