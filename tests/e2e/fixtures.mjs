import { test as base, expect } from "@playwright/test";
import { apiRequestMatchesTarget, assertRuntimeSignalsHealthy, assertStagingAuthConfigured } from "./lib/signals.mjs";
import { assertRequestAllowed, authStateFor, mutationGate, resolveTarget } from "./lib/target.mjs";

const target = resolveTarget(process.env);

export const test = base.extend({
  target: [target, { scope: "worker" }],
  mutationGate: [mutationGate(target, process.env), { scope: "worker" }],
  storageState: async ({ workerStorageState }, use) => use(workerStorageState),
  workerStorageState: [async ({}, use, workerInfo) => {
    const role = String(workerInfo.project.metadata?.role || "anonymous");
    const configuredState = role === "anonymous" ? undefined : authStateFor(role, workerInfo.parallelIndex, process.env);
    assertStagingAuthConfigured(target, role, configuredState);
    const state = role === "anonymous" ? { cookies: [], origins: [] } : configuredState || { cookies: [], origins: [] };
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
    const role = String(testInfo.project.metadata?.role || "anonymous");
    const signals = { consoleErrors: [], pageErrors: [], failedRequests: [], serverErrors: [], blockedMutations: [], apiRequests: [], apiTargetMismatches: [] };
    page.on("console", (message) => {
      if (message.type() === "error") signals.consoleErrors.push(message.text());
    });
    page.on("pageerror", (error) => signals.pageErrors.push(error.message));
    page.on("requestfailed", (request) => {
      const requestUrl = new URL(request.url());
      const allowReason = activeTarget.environment === "local"
        && role === "anonymous"
        && request.method() === "GET"
        && request.resourceType() === "xhr"
        && requestUrl.pathname === "/api/localization/user-preferences"
        && apiRequestMatchesTarget(request.url(), activeTarget.apiBaseUrl)
        ? "local-anonymous-preference-bootstrap"
        : undefined;
      signals.failedRequests.push({ method: request.method(), resourceType: request.resourceType(), url: request.url(), failure: request.failure()?.errorText || "unknown", ...(allowReason ? { allowReason } : {}) });
    });
    page.on("request", (request) => {
      const requestUrl = new URL(request.url());
      if (!requestUrl.pathname.includes("/api/")) return;
      const observed = { method: request.method(), url: request.url() };
      signals.apiRequests.push(observed);
      if (!apiRequestMatchesTarget(request.url(), activeTarget.apiBaseUrl)) {
        signals.apiTargetMismatches.push({ ...observed, expected: activeTarget.apiBaseUrl });
      }
    });
    page.on("response", (response) => {
      if (response.status() >= 500) signals.serverErrors.push({ status: response.status(), url: response.url() });
    });

    // The anonymous local job intentionally has no API process. Fulfil only the
    // global locale bootstrap so every route can be exercised with a strict
    // zero-console-error gate; staging and production always hit their real API.
    if (activeTarget.environment === "local" && role === "anonymous") {
      await page.context().route("**/api/localization/user-preferences", async (route) => {
        if (route.request().method() !== "GET") return route.continue();
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({ success: true, data: [] }),
        });
      });
    }

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
    if (activeTarget.environment === "staging" && role !== "anonymous" && signals.apiRequests.length === 0) {
      signals.apiTargetMismatches.push({ reason: "No application API request was observed", expected: activeTarget.apiBaseUrl });
    }
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
