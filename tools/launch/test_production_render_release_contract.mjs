import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const read = (...parts) => fs.readFileSync(path.join(repository, ...parts), "utf8");

test("Render production cannot auto-deploy a merge ahead of its database", () => {
  const render = read("render.yaml");
  assert.match(render, /autoDeployTrigger:\s*off/);
  assert.match(render, /autoDeploy:\s*false/);
});

test("production release applies owner migrations and Stage101 before exact-SHA deploy", () => {
  const workflow = read(".github", "workflows", "production-render-release.yml");
  const detection = workflow.indexOf("Detect pending lock-sensitive Saudi fleet migration");
  const suspend = workflow.indexOf("node tools/render-service-control.mjs suspend");
  const migration = workflow.indexOf("bash ./tools/apply-canada-ksa-compliance-predeploy.sh");
  const resume = workflow.indexOf("node tools/render-service-control.mjs resume");
  const deploy = workflow.indexOf("node tools/render-deploy-exact.mjs");
  assert.ok(detection >= 0, "lock-sensitive Stage141 detection is missing");
  assert.ok(suspend > detection, "Render must be suspended after the drain decision");
  assert.ok(migration > suspend, "owner migrations must run after the API session drain");
  assert.ok(resume > migration, "Render must be resumed after the migration chain");
  assert.ok(migration >= 0, "Canada/KSA owner migration wrapper is missing");
  assert.ok(deploy > resume, "Render deploy must follow API resumption");
  assert.match(workflow, /environment:\s*production/);
  assert.match(workflow, /ref:\s*\$\{\{ inputs\.candidate_sha \}\}/);
  assert.match(workflow, /NEON_PRODUCTION_OWNER_URI/);
  assert.match(workflow, /RENDER_API_KEY/);
  assert.match(workflow, /if: always\(\) && env\.RENDER_DRAIN_REQUIRED == 'true'/);
  assert.match(workflow, /2026_09_15_stage141_saudi_fleet_operations/);
});

test("Render service drain uses authenticated suspend/resume endpoints and verifies state", () => {
  const helper = read("tools", "render-service-control.mjs");

  assert.match(helper, /new Set\(\["suspend", "resume"\]\)/);
  assert.match(helper, /\/services\/\$\{serviceId\}\/\$\{action\}/);
  assert.match(helper, /method: "POST"/);
  assert.match(helper, /action === "suspend" \? "suspended" : "not_suspended"/);
  assert.match(helper, /Authorization: `Bearer \$\{apiKey\}`/);
  assert.match(helper, /Timed out waiting for Render service state/);
});

test("production release deploys the traceable frontend after the exact API and verifies parity", () => {
  const workflow = read(".github", "workflows", "production-render-release.yml");
  const renderDeploy = workflow.indexOf("node tools/render-deploy-exact.mjs");
  const vercelDeploy = workflow.indexOf("vercel deploy --prod --yes");
  const parity = workflow.indexOf("node tools/verify-production-release-exact.mjs");

  assert.ok(vercelDeploy > renderDeploy, "frontend deployment must follow exact API readiness");
  assert.ok(parity > vercelDeploy, "customer POC parity verification must follow both deployments");
  assert.match(workflow, /npm install --global vercel@59\.11\.7/);
  assert.match(workflow, /--project="\$VERCEL_PROJECT_ID"/);
  assert.match(workflow, /--build-env VITE_DEPLOYMENT_SHA="\$CANDIDATE_SHA"/);
  assert.match(workflow, /--build-env VITE_APP_ENVIRONMENT=production/);
  assert.match(workflow, /--build-env VITE_API_BASE_URL="\$PRODUCTION_API_URL"/);
  assert.doesNotMatch(workflow, /vercel pull|vercel build --prod|--scope="\$VERCEL_ORG_ID"|--token="\$VERCEL_TOKEN"/);
  for (const secret of ["VERCEL_TOKEN", "VERCEL_ORG_ID", "VERCEL_PROJECT_ID"]) {
    assert.match(workflow, new RegExp(`secrets\\.${secret}`));
  }
});

test("Canada/KSA wrapper runs the canonical Stage101/145 chain then verifies stable identities", () => {
  const wrapper = read("tools", "apply-canada-ksa-compliance-predeploy.sh");
  const runner = read("tools", "apply-neon-predeploy-migrations.sh");
  const canonical = wrapper.indexOf("./tools/apply-neon-predeploy-migrations.sh");
  const verification = wrapper.indexOf("DO $verify_stage101$");
  const stage101 = runner.indexOf("2026_09_03_stage101_canada_ksa_compliance_baseline");
  const stage145 = runner.indexOf("2026_09_15_stage145_canonical_market_reference_reconciliation");

  assert.ok(canonical >= 0, "canonical owner migration chain must still run");
  assert.ok(stage101 >= 0, "canonical migration chain must include Stage101");
  assert.ok(stage145 > stage101, "stable-key reconciliation must follow the Stage101 baseline");
  assert.ok(verification > canonical, "reference verification must follow the canonical chain");
  assert.match(runner, /schema_migrations/);
  assert.match(wrapper, /External provider\/device\/certification\/qualification evidence: STILL REQUIRED/);
});

test("production migrations bound live DDL lock waits and retry transient contention", () => {
  const runner = read("tools", "apply-neon-predeploy-migrations.sh");
  const helper = runner.slice(
    runner.indexOf("apply_migration_file()"),
    runner.indexOf("reapply_late_control_boundaries()"),
  );

  assert.match(runner, /MIGRATION_LOCK_MAX_ATTEMPTS=20/);
  assert.match(runner, /MIGRATION_LOCK_RETRY_DELAY_SECONDS=2/);
  assert.match(helper, /local lock_timeout="\$\{3:-3s\}"/);
  assert.match(helper, /SET lock_timeout='\$lock_timeout'/);
  assert.match(helper, /deadlock detected/);
  assert.match(helper, /canceling statement due to lock timeout/);
  assert.match(helper, /return "\$status"/);
  assert.match(
    runner,
    /2026_09_11_stage139_telemetry_ledger_backfill_reconciliation[\s\S]*?apply_migration_file "\$f" "\$m" "30s" 4/,
  );

  for (const stage of ["Stage58", "Stage59", "Stage67", "Stage76"]) {
    assert.match(runner, new RegExp(`apply_migration_file [^\\n]+ ${stage}`));
  }
  assert.match(runner, /apply_migration_file "\$f" "\$m"/);
});

test("ledgered migrations are verified without replaying broad DDL on live tables", () => {
  const runner = read("tools", "apply-neon-predeploy-migrations.sh");

  assert.doesNotMatch(runner, /repair_migration/);
  assert.doesNotMatch(runner, /ledgered reconciliation — reapplying to repair drift/);
  assert.match(runner, /already applied \(ledger\) — verifying without replay/);
  assert.match(runner, /if \[ "\$applied" = "1" \]; then[\s\S]*?continue[\s\S]*?echo "── applying \$m"/);
  assert.match(runner, /reapply_late_control_boundaries/);
  assert.match(runner, /reapply_immutable_evidence_offboarding_boundaries/);
  const offboardingReconcile = runner.slice(
    runner.indexOf("reapply_immutable_evidence_offboarding_boundaries()"),
    runner.indexOf("MIGRATIONS=("),
  );
  assert.match(offboardingReconcile, /stage72_hos_offboarding_immutability_reconciliation\.sql/);
  assert.match(offboardingReconcile, /Stage72/);
  assert.match(offboardingReconcile, /stage73_hos_offboarding_null_fail_closed\.sql/);
  assert.match(offboardingReconcile, /Stage73/);
  assert.match(runner, /Fleet Stage55 authorization evidence contract drifted/);
  assert.match(runner, /Stage54\/55\/56\/57 migration ledger missing or duplicated/);
});

test("migration-only databases reconcile legacy operational columns before Stage135 cleanup", () => {
  const runner = read("tools", "apply-neon-predeploy-migrations.sh");
  const podContract = read(
    "database",
    "migrations",
    "2026_09_11_stage137_legacy_operational_truth_contract.sql",
  );
  const contractIndex = runner.indexOf("2026_09_11_stage137_legacy_operational_truth_contract");
  const cleanupIndex = runner.indexOf("2026_09_10_stage135_demo_operational_truth_reconciliation");

  assert.ok(contractIndex >= 0);
  assert.ok(cleanupIndex > contractIndex);
  assert.match(podContract, /ALTER TABLE jobs/);
  assert.match(podContract, /ADD COLUMN IF NOT EXISTS job_number/);
  assert.match(podContract, /ADD COLUMN IF NOT EXISTS deleted_at/);
  assert.match(podContract, /ALTER TABLE proof_of_delivery/);
  assert.match(podContract, /ADD COLUMN IF NOT EXISTS proof_type/);
  assert.match(podContract, /ADD COLUMN IF NOT EXISTS notes/);
  assert.match(podContract, /2026_09_11_stage137_legacy_operational_truth_contract/);
});

test("ledger backfills are reconciled with a new forward-only telemetry migration", () => {
  const runner = read("tools", "apply-neon-predeploy-migrations.sh");
  const migration = read(
    "database",
    "migrations",
    "2026_09_11_stage139_telemetry_ledger_backfill_reconciliation.sql",
  );

  assert.match(runner, /2026_09_11_stage139_telemetry_ledger_backfill_reconciliation/);
  assert.match(runner, /Stage139 telemetry live-state contract drifted/);
  assert.match(migration, /ALTER TABLE IF EXISTS telemetry_alerts/);
  assert.match(migration, /ADD COLUMN IF NOT EXISTS correlation_id/);
  assert.match(migration, /CREATE TABLE IF NOT EXISTS telemetry_live_asset_states/);
  assert.match(migration, /Stage139 telemetry live-state contract is incomplete/);
  assert.match(migration, /2026_09_11_stage139_telemetry_ledger_backfill_reconciliation/);
});

test("Canada/KSA release ordering uses stable market keys rather than fixed seed IDs", () => {
  const wrapper = read("tools", "apply-canada-ksa-compliance-predeploy.sh");
  const runtime = read("backend-dotnet", "Services", "Batch6SchemaService.cs");
  const reconciliation = read(
    "database",
    "migrations",
    "2026_09_15_stage145_canonical_market_reference_reconciliation.sql",
  );

  assert.match(runtime, /WHERE p\.country_code=s\.country_code AND p\.profile_name=s\.profile_name/);
  assert.match(runtime, /FROM resolved s WHERE r\.rule_code=s\.rule_code/);
  assert.match(runtime, /WHERE NOT EXISTS \(SELECT 1 FROM compliance_rules r WHERE r\.rule_code=s\.rule_code\)/);
  assert.doesNotMatch(runtime, /\(3,'CA','Canada Federal HOS - South of 60N'/);
  assert.doesNotMatch(runtime, /\(4,'SA','Saudi TGA Goods Transport HOS'/);

  assert.match(reconciliation, /Canada Federal HOS - South of 60N/);
  assert.match(reconciliation, /Saudi TGA Goods Transport HOS/);
  assert.match(reconciliation, /CA-S60-HOS-13H-DRIVE/);
  assert.match(reconciliation, /CA-CARRIER-SAFETY-FITNESS/);
  assert.match(reconciliation, /SA-TGA-HOS-9H-DRIVE/);
  assert.match(wrapper, /Migration\/runtime stable-key compatibility: VERIFIED/);
});

test("runtime manifest never receives the owner migration credential", () => {
  const render = read("render.yaml");
  assert.doesNotMatch(render, /NEON_PG_URI|NEON_PRODUCTION_OWNER_URI/);
});

test("Render deploy helper verifies the exact healthy candidate", () => {
  const helper = read("tools", "render-deploy-exact.mjs");
  assert.match(helper, /commitId:\s*candidateSha/);
  assert.match(helper, /body\?\.status === "ready"/);
  assert.match(helper, /body\?\.version === candidateSha/);
  assert.match(helper, /pre_deploy_failed/);
  assert.match(helper, /update_failed/);
});

test("customer POC parity helper requires the frontend and API to report the candidate", () => {
  const helper = read("tools", "verify-production-release-exact.mjs");
  assert.match(helper, /new URL\("\/deployment\.json", frontendUrl\)/);
  assert.match(helper, /manifest\?\.frontendSha === candidateSha/);
  assert.match(helper, /manifest\?\.frontendEnvironment === "production"/);
  assert.match(helper, /health\?\.version === candidateSha/);
});
