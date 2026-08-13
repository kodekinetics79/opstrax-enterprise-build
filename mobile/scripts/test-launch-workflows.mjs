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

test("driver safety uses authenticated assignment identity and server-enforced departure gates", async () => {
  const [client, screen, navigation] = await Promise.all([
    source("src/api/client.ts"),
    source("src/screens/DriverSafetyScreen.tsx"),
    source("src/navigation/RootNavigator.tsx"),
  ]);
  assert.match(client, /currentDriverAssignment:[\s\S]*\/api\/driver\/assignments\/current/);
  assert.match(client, /confirmDriverVehicle:[\s\S]*confirm-vehicle/);
  assert.match(client, /submitDriverDvir:[\s\S]*Idempotency-Key/);
  assert.match(client, /updateDriverAssignmentStatus:[\s\S]*\/status/);
  assert.match(screen, /driverMe\(\)/);
  assert.match(screen, /currentAssignment\.vehicleId/);
  assert.doesNotMatch(screen, /vehicle id input/i);
  assert.match(screen, /unit_suffix/);
  assert.match(screen, /vin_suffix/);
  assert.match(screen, /assignment\.vehicleUnitSuffixLength/);
  assert.match(screen, /confirmationReference\.trim\(\)\.length !== confirmationLength/);
  assert.match(screen, /Boolean\(item\.isRequired\) \? \[\] : \["na"\]/);
  assert.match(screen, /DvirDriverAttestation/);
  assert.match(screen, /attestationAccepted: true/);
  assert.match(screen, /safePretripReady/);
  assert.match(screen, /nextStatus === "en_route_pickup"/);
  assert.match(navigation, /hasPermission\("driver:self"\)/);
  assert.match(navigation, /canUseDriverSafety \? <Tabs\.Screen/);
});

test("mobile screens consume the actual nested API contracts", async () => {
  const [client, proof, telemetry] = await Promise.all([
    source("src/api/client.ts"),
    source("src/screens/ProofScreen.tsx"),
    source("src/screens/TelemetryScreen.tsx"),
  ]);
  for (const method of ["smartAssignmentRecommendations", "siteAccess", "pickupAuthorizations", "warehouseHandovers"]) {
    assert.match(client, new RegExp(`${method}:[\\s\\S]*request\\.get<\\{ items: JsonRecord\\[\\] \\}>`));
  }
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
  assert.match(navigation, /canUseProof \? <Tabs\.Screen/);
  assert.match(navigation, /canUseFleetHealth \? <Tabs\.Screen/);
  assert.match(workflow, /session\.company\.id/);
  assert.match(workflow, /session\.user\.id/);
});

test("telemetry backend error payloads cannot render as zero-valued live truth", async () => {
  const telemetry = await source("src/screens/TelemetryScreen.tsx");
  assert.match(telemetry, /telemetryPayloadError/);
  assert.match(telemetry, /telemetry\.error \|\| telemetryPayloadError/);
  assert.match(telemetry, /<ErrorState title="Telemetry unavailable"/);
  assert.doesNotMatch(telemetry, /telemetry\.error[^\n]*<EmptyState[^\n]*\n\s*\{telemetry\.data/);
});

test("mobile data calls and routes are permission-gated", async () => {
  const [dashboard, workflow, telemetry, navigation] = await Promise.all([
    source("src/screens/DashboardScreen.tsx"),
    source("src/screens/WorkflowScreen.tsx"),
    source("src/screens/TelemetryScreen.tsx"),
    source("src/navigation/RootNavigator.tsx"),
  ]);
  assert.match(dashboard, /canReadJobs \? api\.jobs\(\) : null/);
  assert.match(dashboard, /selectedJobId && canReadSummary/);
  assert.match(workflow, /selectedJobId && canReadRecommendations/);
  assert.match(workflow, /selectedJobId && canReadSiteAccess/);
  assert.match(telemetry, /canReadTelemetry \? api\.telemetrySummary\(\) : null/);
  assert.match(navigation, /const canUseWorkflows/);
});

test("proof actions expose lifecycle and evidence blockers instead of silent no-ops", async () => {
  const proof = await source("src/screens/ProofScreen.tsx");
  assert.match(proof, /\["draft", "rejected"\]\.includes\(proofStatus\) && artifacts\.length > 0/);
  assert.match(proof, /proofStatus === "submitted"/);
  assert.match(proof, /disabled=\{!canSubmitState\}/);
  assert.match(proof, /disabled=\{!canValidateState\}/);
});

test("mobile loading, errors, headings, and MFA code expose accessibility metadata", async () => {
  const [ui, login] = await Promise.all([source("src/components/ui.tsx"), source("src/screens/LoginScreen.tsx")]);
  assert.match(ui, /accessibilityRole="header"/);
  assert.match(ui, /accessibilityRole="alert"/);
  assert.match(ui, /accessibilityLiveRegion="polite"/);
  assert.match(login, /autoComplete="one-time-code"/);
  assert.match(login, /textContentType="oneTimeCode"/);
});
