import { test as base, expect } from "@playwright/test";
import { assertRuntimeSignalsHealthy } from "./lib/signals.mjs";
import { assertRequestAllowed, authStateFor, mutationGate, resolveTarget } from "./lib/target.mjs";

const target = resolveTarget(process.env);

export const test = base.extend({
  target: [target, { scope: "worker" }],
  mutationGate: [mutationGate(target, process.env), { scope: "worker" }],
  storageState: async ({ workerStorageState }, use) => use(workerStorageState),
  workerStorageState: [async ({}, use, workerInfo) => {
    const role = String(workerInfo.project.metadata?.role || "anonymous");
    const state = role === "anonymous"
      ? { cookies: [], origins: [] }
      : authStateFor(role, workerInfo.parallelIndex, process.env) || { cookies: [], origins: [] };
    await use(state);
  }, { scope: "worker" }],
  authConfigured: [async ({}, use, testInfo) => {
    const role = String(testInfo.project.metadata?.role || "anonymous");
    await use(role === "anonymous" || Boolean(authStateFor(role, testInfo.parallelIndex, process.env)));
  }, { scope: "worker" }],
  runId: [async ({}, use, testInfo) => {
    const seed = process.env.GITHUB_RUN_ID || process.env.BUILD_BUILDID || String(Date.now());
    const safe = `${seed}-${testInfo.parallelIndex}`.replace(/[^a-zA-Z0-9-]/g, "-");
    await use(safe);
  }, { scope: "worker" }],
  clientSignals: [async ({ page, target: activeTarget }, use, testInfo) => {
    const signals = { consoleErrors: [], pageErrors: [], failedRequests: [], serverErrors: [], blockedMutations: [] };
    page.on("console", (message) => {
      if (message.type() === "error") signals.consoleErrors.push(message.text());
    });
    page.on("pageerror", (error) => signals.pageErrors.push(error.message));
    page.on("requestfailed", (request) => {
      signals.failedRequests.push({ method: request.method(), url: request.url(), failure: request.failure()?.errorText || "unknown" });
    });
    page.on("response", (response) => {
      if (response.status() >= 500) signals.serverErrors.push({ status: response.status(), url: response.url() });
    });

    if (activeTarget.isProduction) {
      await page.context().route("**/*", async (route) => {
        try {
          assertRequestAllowed(activeTarget, route.request().method());
          await route.continue();
        } catch (error) {
          signals.blockedMutations.push({ method: route.request().method(), url: route.request().url() });
          await route.abort("blockedbyclient");
        }
      });
    }

    await use(signals);
    if (Object.values(signals).some((items) => items.length > 0)) {
      await testInfo.attach("browser-signals.json", {
        body: Buffer.from(JSON.stringify(signals, null, 2)),
        contentType: "application/json",
      });
    }
    expect(signals.blockedMutations, "A production browser journey attempted a non-GET request").toEqual([]);
    assertRuntimeSignalsHealthy(signals);
  }, { auto: true }],
});

export { expect } from "@playwright/test";
