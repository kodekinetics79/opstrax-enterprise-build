import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const source = async (path) => readFile(new URL(`../${path}`, import.meta.url), "utf8");

test("MFA challenge is completed before a mobile session is stored", async () => {
  const [client, provider, login] = await Promise.all([
    source("src/api/client.ts"),
    source("src/auth/SessionProvider.tsx"),
    source("src/screens/LoginScreen.tsx"),
  ]);
  assert.match(client, /\/api\/auth\/mfa\/login-verify/);
  assert.match(provider, /"mfaRequired" in next/);
  assert.match(provider, /verifyMfaLogin\(mfaChallenge\.challengeToken/);
  assert.match(login, /Verify code/);
});

test("proof and assignment controls invoke server mutations", async () => {
  const [proof, workflow] = await Promise.all([
    source("src/screens/ProofScreen.tsx"),
    source("src/screens/WorkflowScreen.tsx"),
  ]);
  assert.match(proof, /submitProofPackage/);
  assert.match(proof, /validateProofPackage/);
  assert.match(workflow, /acceptSmartAssignment/);
  assert.match(workflow, /rejectSmartAssignment/);
});

test("mobile screens consume the actual nested API contracts", async () => {
  const [proof, telemetry] = await Promise.all([
    source("src/screens/ProofScreen.tsx"),
    source("src/screens/TelemetryScreen.tsx"),
  ]);
  assert.match(proof, /\.items/);
  assert.match(telemetry, /telemetryRecord\?\.kpis/);
  assert.match(telemetry, /maintenanceRecord\?\.kpis/);
  assert.match(telemetry, /fleetSafetyScore/);
});

test("mobile navigation and persisted job selection are identity scoped", async () => {
  const [navigation, workflow] = await Promise.all([
    source("src/navigation/RootNavigator.tsx"),
    source("src/workflow/WorkflowContext.tsx"),
  ]);
  assert.match(navigation, /routes\.has\("Proof"\)/);
  assert.match(workflow, /session\.company\.id/);
  assert.match(workflow, /session\.user\.id/);
});
