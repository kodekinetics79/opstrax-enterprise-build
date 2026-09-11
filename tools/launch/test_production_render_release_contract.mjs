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
  const migration = workflow.indexOf("bash ./tools/apply-canada-ksa-compliance-predeploy.sh");
  const deploy = workflow.indexOf("node tools/render-deploy-exact.mjs");
  assert.ok(migration >= 0, "Canada/KSA owner migration wrapper is missing");
  assert.ok(deploy > migration, "Render deploy must follow the successful migration chain");
  assert.match(workflow, /environment:\s*production/);
  assert.match(workflow, /ref:\s*\$\{\{ inputs\.candidate_sha \}\}/);
  assert.match(workflow, /NEON_PRODUCTION_OWNER_URI/);
  assert.match(workflow, /RENDER_API_KEY/);
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

test("Canada/KSA wrapper preserves canonical chain then applies and verifies Stage101", () => {
  const wrapper = read("tools", "apply-canada-ksa-compliance-predeploy.sh");
  const canonical = wrapper.indexOf("./tools/apply-neon-predeploy-migrations.sh");
  const stage101 = wrapper.indexOf("2026_09_03_stage101_canada_ksa_compliance_baseline.sql");
  const verification = wrapper.indexOf("DO $verify_stage101$");

  assert.ok(canonical >= 0, "canonical owner migration chain must still run");
  assert.ok(stage101 > canonical, "Stage101 must run after the canonical predecessor chain");
  assert.ok(verification > stage101, "Stage101 post-deploy verification must follow its application");
  assert.match(wrapper, /schema_migrations/);
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
  assert.match(helper, /SET lock_timeout='3s'/);
  assert.match(helper, /deadlock detected/);
  assert.match(helper, /canceling statement due to lock timeout/);
  assert.match(helper, /return "\$status"/);

  for (const stage of ["Stage58", "Stage59", "Stage67", "Stage76"]) {
    assert.match(runner, new RegExp(`apply_migration_file [^\\n]+ ${stage}`));
  }
  assert.match(runner, /apply_migration_file "\$f" "\$m"/);
});

test("ledgered Stage55 is verified without replaying broad DDL on live tables", () => {
  const runner = read("tools", "apply-neon-predeploy-migrations.sh");
  const repairSelection = runner.slice(
    runner.indexOf("repair_migration=false"),
    runner.indexOf("if [ \"$applied\" = \"1\" ] && [ \"$repair_migration\" = false ]"),
  );

  assert.doesNotMatch(repairSelection, /2026_07_30_stage55_fleet_runtime_route_contract/);
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

test("Canada/KSA wrapper preserves Batch6 fixed-ID seed contract across release ordering", () => {
  const wrapper = read("tools", "apply-canada-ksa-compliance-predeploy.sh");
  const runtime = read("backend-dotnet", "Services", "Batch6SchemaService.cs");

  // Runtime reference seeding reserves these original identities and uses
  // conflict-ignore semantics. The predeploy wrapper must therefore reserve the
  // Canada/KSA identities before Stage101 adds generated profiles/rules.
  assert.match(runtime, /\(3,'CA','/);
  assert.match(runtime, /\(4,'SA','/);
  assert.match(runtime, /\(6,3,'/);
  assert.match(runtime, /\(7,3,'/);
  assert.match(runtime, /\(8,4,'/);
  assert.match(runtime, /ON CONFLICT DO NOTHING/);

  assert.match(wrapper, /\(3,'CA','Canada Federal HOS - South of 60N'/);
  assert.match(wrapper, /\(4,'SA','Saudi TGA Goods Transport HOS'/);
  assert.match(wrapper, /\(6,3,'CA-S60-HOS-13H-DRIVE'/);
  assert.match(wrapper, /\(7,3,'CA-CARRIER-SAFETY-FITNESS'/);
  assert.match(wrapper, /\(8,4,'SA-TGA-HOS-9H-DRIVE'/);
  assert.match(wrapper, /pg_get_serial_sequence\('compliance_profiles','id'\)/);
  assert.match(wrapper, /pg_get_serial_sequence\('compliance_rules','id'\)/);
  assert.match(wrapper, /Runtime fixed-ID compatibility: VERIFIED/);
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
