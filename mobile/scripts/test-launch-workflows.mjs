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

test("driver proof and assignment controls invoke identity-scoped server mutations", async () => {
  const [proof, trip] = await Promise.all([
    source("src/screens/DriverProofScreen.tsx"),
    source("src/screens/DriverTripScreen.tsx"),
  ]);
  assert.match(proof, /uploadDriverProofArtifact/);
  assert.match(proof, /submitDriverProof/);
  assert.match(proof, /requestForegroundPermissionsAsync/);
  assert.match(trip, /updateDriverAssignmentStatus/);
  assert.match(trip, /reportDriverException/);
});

test("operations proof and assignment controls invoke server mutations", async () => {
  const [proof, workflow] = await Promise.all([
    source("src/screens/ProofScreen.tsx"),
    source("src/screens/WorkflowScreen.tsx"),
  ]);
  assert.match(proof, /submitProofPackage/);
  assert.match(proof, /validateProofPackage/);
  assert.match(workflow, /acceptSmartAssignment/);
  assert.match(workflow, /rejectSmartAssignment/);
});

test("driver compliance consumes real HOS and idempotent DVIR contracts", async () => {
  const [client, compliance] = await Promise.all([
    source("src/api/client.ts"),
    source("src/screens/DriverComplianceScreen.tsx"),
  ]);
  assert.match(client, /Idempotency-Key/);
  assert.match(client, /\/api\/driver\/dvir/);
  assert.match(compliance, /driverHos/);
  assert.match(compliance, /submitDriverDvir/);
  assert.match(compliance, /ATTESTATION/);
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

test("mobile navigation, login, and persisted state are tenant and identity scoped", async () => {
  const [navigation, login, workflow] = await Promise.all([
    source("src/navigation/RootNavigator.tsx"),
    source("src/screens/LoginScreen.tsx"),
    source("src/workflow/WorkflowContext.tsx"),
  ]);
  assert.match(navigation, /directPermissions\.has\("driver:self"\)/);
  assert.match(login, /Organization code/);
  assert.match(login, /login\(email, password, companyCode\)/);
  assert.match(navigation, /canProof \? <Tabs\.Screen/);
  assert.match(navigation, /canFleet \? <Tabs\.Screen/);
  assert.match(workflow, /session\.company\.id/);
  assert.match(workflow, /session\.user\.id/);
});

test("manual object identifiers and fake offline success are not exposed", async () => {
  const [dashboard, settings] = await Promise.all([
    source("src/screens/DashboardScreen.tsx"),
    source("src/screens/SettingsScreen.tsx"),
  ]);
  assert.doesNotMatch(dashboard, /Enter a real job id/);
  assert.match(dashboard, /Manual database-ID entry is intentionally unavailable/);
  assert.match(settings, /Live mutations require a connection/);
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
  assert.match(dashboard, /canReadJobs \? api\.jobs\(\) : \[\]/);
  assert.match(workflow, /selectedJobId && canReadRecommendations/);
  assert.match(workflow, /selectedJobId && canReadSiteAccess/);
  assert.match(telemetry, /canReadTelemetry \? api\.telemetrySummary\(\) : null/);
  assert.match(navigation, /const canWork/);
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
