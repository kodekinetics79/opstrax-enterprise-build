import { defineConfig, devices } from "@playwright/test";
import { loadLocalEnv } from "./lib/env.mjs";
import { iotLifecycleGate, mutationGate, resolveTarget } from "./lib/target.mjs";

loadLocalEnv();
const target = resolveTarget(process.env);
const gate = mutationGate(target, process.env);
const iotGate = iotLifecycleGate(target, process.env);

function roleProject(name, file, role) {
  return {
    name,
    testMatch: file,
    use: {
      ...devices["Desktop Chrome"],
    },
    metadata: { role, targetEnvironment: target.environment },
  };
}

const projects = [
  {
    name: "public-chromium",
    testMatch: ["**/public.spec.mjs", "**/anonymous-boundaries.spec.mjs"],
    use: {
      ...devices["Desktop Chrome"],
      storageState: { cookies: [], origins: [] },
    },
    metadata: { role: "anonymous", targetEnvironment: target.environment },
  },
];

if (!target.isProduction) {
  projects.push(
    roleProject("tenant-readonly", "**/tenant.readonly.spec.mjs", "tenant"),
    roleProject("driver-readonly", "**/driver.readonly.spec.mjs", "driver"),
    roleProject("customer-readonly", "**/customer.readonly.spec.mjs", "customer"),
    roleProject("platform-readonly", "**/platform.readonly.spec.mjs", "platform"),
    {
      ...roleProject("staging-mutations", "**/staging.mutations.spec.mjs", "tenant"),
      workers: 1,
      metadata: { role: "tenant", targetEnvironment: target.environment, mutationGate: gate.enabled },
    },
    {
      ...roleProject("staging-iot-lifecycle", "**/staging.iot-lifecycle.spec.mjs", "tenant"),
      workers: 1,
      use: {
        ...devices["Desktop Chrome"],
        // Provisioning returns credentials exactly once and device ingest carries
        // those credentials in headers. Never persist them in a Playwright trace.
        trace: "off",
        video: "off",
      },
      metadata: { role: "tenant", targetEnvironment: target.environment, mutationGate: iotGate.enabled },
    },
  );
}

export default defineConfig({
  testDir: "./specs",
  outputDir: "./test-results",
  fullyParallel: true,
  forbidOnly: Boolean(process.env.CI),
  retries: 0,
  workers: process.env.CI ? 2 : undefined,
  timeout: 45_000,
  expect: { timeout: 10_000 },
  reporter: process.env.CI
    ? [["line"], ["html", { outputFolder: "playwright-report", open: "never" }]]
    : [["list"], ["html", { outputFolder: "playwright-report", open: "never" }]],
  use: {
    baseURL: target.uiBaseUrl,
    actionTimeout: 10_000,
    navigationTimeout: 20_000,
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
    video: "retain-on-failure",
  },
  projects,
  metadata: {
    targetEnvironment: target.environment,
    uiBaseUrl: target.uiBaseUrl,
    apiBaseUrl: target.apiBaseUrl,
    productionGetOnly: target.isProduction,
  },
});
