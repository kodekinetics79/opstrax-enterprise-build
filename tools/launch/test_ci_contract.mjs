import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const read = (name) => fs.readFileSync(path.join(repository, name), "utf8");

function assertOrdered(source, markers) {
  let previous = -1;
  for (const marker of markers) {
    const current = source.indexOf(marker, previous + 1);
    assert.ok(current > previous, `${marker} missing or out of order`);
    previous = current;
  }
}

test("CI reapplies Stage76 after Stage58, Stage59 and Stage67", () => {
  const workflow = read(".github/workflows/ci.yml");
  const terminalStep = workflow.slice(
    workflow.indexOf("Reapply mandatory terminal tenant and telemetry boundaries"),
    workflow.indexOf("Run DB-backed integration suites"),
  );
  assertOrdered(terminalStep, [
    "2026_07_31_stage58_nonforgeable_tenant_ticket.sql",
    "2026_07_31_stage59_data_protection_key_ring.sql",
    "2026_08_02_stage67_telematics_diagnostics_integrity.sql",
    "2026_08_11_stage76_telematics_security_hardening.sql",
  ]);
  assert.match(terminalStep, /version='2026_08_11_stage76_telematics_security_hardening'/);
  assert.match(terminalStep, /defaclnamespace=0 OR n\.nspname='public'/);
  assert.match(terminalStep, /canonical_telemetry_events_id_seq/);
  assert.match(terminalStep, /telemetry_replay_device_state/);
});

test("predeploy runner makes Stage76 terminal on first and repair runs", () => {
  const runner = read("tools/apply-neon-predeploy-migrations.sh");
  const repair = runner.slice(runner.indexOf('if [ "$stage58_already_applied" = "1" ]'), runner.indexOf("Post-check: auth-critical columns"));
  assertOrdered(repair, [
    "2026_07_31_stage58_nonforgeable_tenant_ticket.sql",
    "2026_07_31_stage59_data_protection_key_ring.sql",
    "2026_08_02_stage67_telematics_diagnostics_integrity.sql",
    "2026_08_11_stage76_telematics_security_hardening.sql",
  ]);
  const initial = runner.slice(runner.indexOf("Reapplying Stage67 least-privilege"));
  assertOrdered(initial, [
    "2026_08_02_stage67_telematics_diagnostics_integrity.sql",
    "2026_08_11_stage76_telematics_security_hardening.sql",
    "Ledger:",
  ]);
});

test("protected clean database receives the complete pre-RLS runtime foundation", () => {
  const runner = read("tools/apply-neon-predeploy-migrations.sh");
  const clean = read("tools/test-predeploy-clean-chain.sh");
  const stage77 = read("database/migrations/2026_08_12_stage77_protected_role_bootstrap.sql");
  for (const migration of [
    "2026_06_27_stage5_p0b1a_foundation",
    "2026_06_28_stage5b_p0b1a2_persistence_hardening",
    "2026_06_28_stage5d_p0b1a3_dispatcher",
    "2026_06_28_stage6_p0b1b_business_spine",
    "2026_06_28_stage7a_revenue_readiness_schema_contract",
    "2026_06_28_stage8_finance_activation",
    "2026_06_28_stage12a_telemetry_live_state",
    "2026_06_28_stage13b_safety_maintenance_foundation",
    "2026_06_29_stage18_commercial_foundation",
    "2026_08_13_stage78_country_profiles_runtime_contract",
  ]) {
    assert.match(runner, new RegExp(migration));
    assert.match(clean, new RegExp(migration));
  }
  assert.match(stage77, /ADD COLUMN IF NOT EXISTS invite_token_hash/);
  assert.match(stage77, /ADD COLUMN IF NOT EXISTS mfa_secret/);
  const stage78 = read("database/migrations/2026_08_13_stage78_country_profiles_runtime_contract.sql");
  assert.match(stage78, /CREATE TABLE IF NOT EXISTS country_profiles/);
  assert.match(stage78, /'US','United States','USD'/);
  assert.match(stage78, /'SA','Saudi Arabia','SAR'.*'rtl'/);
  assert.match(runner, /Stage78 country-profile runtime catalog is incomplete/);
  assert.match(runner, /to_regclass\('public\.outbox_messages'\) IS NULL/);
  assert.ok(runner.indexOf("2026_07_30_stage51_production_runtime_support") < runner.indexOf("2026_06_28_stage12a_telemetry_live_state"));
  const commercial = read("database/migrations/2026_06_29_stage18_commercial_foundation.sql");
  assert.match(commercial, /ADD COLUMN IF NOT EXISTS contract_number/);
  assert.match(commercial, /ALTER COLUMN contract_number SET NOT NULL/);
  assert.match(commercial, /stage18_sync_contract_compatibility/);
  assert.match(commercial, /NEW\.contract_code := NEW\.contract_number/);
  assert.match(commercial, /NEW\.contract_number := NEW\.contract_code/);
  assert.match(commercial, /NEW\.expiration_date := NEW\.expiry_date/);
  assert.match(clean, /Stage18 did not project the legacy contract write shape/);
  assert.match(clean, /Stage18 did not project the modern contract write shape/);
  assert.match(clean, /Stage18 did not synchronize legacy contract updates/);
  const foundation = read("database/migrations/2026_06_27_stage5_p0b1a_foundation.sql");
  assert.match(foundation, /ALTER TABLE ai_recommendations ADD COLUMN IF NOT EXISTS tenant_id/);
  assert.match(foundation, /SET company_id=COALESCE\(company_id, tenant_id\)/);
  assert.match(foundation, /ALTER TABLE ai_recommendations ALTER COLUMN tenant_id SET NOT NULL/);
  const businessSpine = read("database/migrations/2026_06_28_stage6_p0b1b_business_spine.sql");
  assert.match(businessSpine, /CREATE TABLE IF NOT EXISTS trip_stops/);
  assert.ok(businessSpine.indexOf("CREATE TABLE IF NOT EXISTS trip_stops") < businessSpine.indexOf("ALTER TABLE trip_stops ADD COLUMN"));
});

test("clean chain and production rehearsal require Stage76 evidence", () => {
  const clean = read("tools/test-predeploy-clean-chain.sh");
  const rehearsal = read("tools/test-production-shaped-local-rehearsal.sh");
  assert.match(clean, /2026_08_11_stage76_telematics_security_hardening/);
  assert.match(clean, /Stage76-terminal runner replays/);
  assert.match(rehearsal, /migration_ledgers=16/);
  assert.match(rehearsal, /stage76_secret_read_violations=0/);
  assert.match(rehearsal, /stage76_default_acl_violations=0/);
  assert.match(rehearsal, /stage76_runtime_acl_violations=0/);
  assert.match(rehearsal, /stage76_app_column_acl_violations=0/);
  assert.match(rehearsal, /stage76_sequence_acl_violations=0/);
  assert.match(rehearsal, /stage76_replay_schema_violations=0/);
  assert.match(clean, /uq_telemetry_replay_seen_unwrapped/);
  assert.match(clean, /malfunction_resolved_at/);
});

test("new mobile, launch-tooling and Playwright jobs are exact-SHA mandatory gates", () => {
  const workflow = read(".github/workflows/ci.yml");
  const evidence = workflow.slice(workflow.indexOf("exact-sha-release-evidence:"));
  const validator = read("tools/validate-mandatory-ci-gates.sh");
  for (const job of ["mobile-build-test", "launch-tooling-tests", "playwright-public-tests"]) {
    assert.match(evidence, new RegExp(`- ${job}`));
    assert.match(evidence, new RegExp(`printf '${job}\\\\t`));
    assert.match(validator, new RegExp(`expected\\[\"${job}\"\\] = 1`));
  }
});

test("pull request release evidence is bound to the immutable branch head", () => {
  const workflow = read(".github/workflows/ci.yml");
  assert.match(
    workflow,
    /CANDIDATE_SHA: \$\{\{ github\.event\.pull_request\.head\.sha \|\| github\.sha \}\}/,
  );
  const checkoutCount = (workflow.match(/uses: actions\/checkout@[0-9a-f]{40}/g) || []).length;
  const candidateRefCount = (workflow.match(/ref: \$\{\{ env\.CANDIDATE_SHA \}\}/g) || []).length;
  assert.ok(checkoutCount > 0, "workflow has no source checkouts");
  assert.equal(candidateRefCount, checkoutCount, "every job must check out the candidate head");
  assert.match(workflow, /name: release-provenance-\$\{\{ env\.CANDIDATE_SHA \}\}/);
  assert.match(workflow, /name: opstrax-release-candidate-\$\{\{ env\.CANDIDATE_SHA \}\}/);
  assert.doesNotMatch(workflow, /name: (?:release-provenance|opstrax-release-candidate)-\$\{\{ github\.sha \}\}/);
  assert.doesNotMatch(workflow, /git rev-parse HEAD\)" = "\$GITHUB_SHA"/);
});

test("release container Telematics tests have a hermetic Postgres service", () => {
  const workflow = read(".github/workflows/ci.yml");
  const release = workflow.slice(
    workflow.indexOf("release-container-builds:"),
    workflow.indexOf("exact-sha-release-evidence:"),
  );
  assert.match(release, /services:\n\s+postgres:/);
  assert.match(release, /postgres:17@sha256:/);
  assert.match(release, /--health-cmd "pg_isready -U zayra -d opstrax_local"/);
  assert.match(release, /OPSTRAX_TEST_DB: "Host=127\.0\.0\.1;Port=5433;Database=opstrax_local;Username=zayra;Password=[^"]+"/);
  assert.match(release, /dotnet test telematics\/Opstrax\.Telematics\.sln/);
});

test("release API image contains the required gateway and terminal migrations", () => {
  const dockerfile = read("backend-dotnet/Dockerfile");
  const workflow = read(".github/workflows/ci.yml");
  const release = workflow.slice(
    workflow.indexOf("release-container-builds:"),
    workflow.indexOf("exact-sha-release-evidence:"),
  );
  for (const migration of [
    "2026_07_16_stage42_telemetry_gateways.sql",
    "2026_08_11_stage76_telematics_security_hardening.sql",
    "2026_08_12_stage77_protected_role_bootstrap.sql",
  ]) {
    assert.match(dockerfile, new RegExp(`COPY database/migrations/${migration} database/migrations/`));
    assert.match(release, new RegExp(`test -f database/migrations/${migration}`));
    assert.match(release, new RegExp(`docker cp \\\"\\$api_container:/app/Migrations/${migration}\\\"`));
    assert.match(release, new RegExp(`cmp database/migrations/${migration}`));
  }
});

test("Playwright bundle and browser use the same local API origin", () => {
  const workflow = read(".github/workflows/ci.yml");
  const job = workflow.slice(
    workflow.indexOf("playwright-public-tests:"),
    workflow.indexOf("dotnet-build-test:"),
  );
  assert.match(job, /name: Build frontend against the local browser-test API origin[\s\S]*VITE_API_BASE_URL: http:\/\/127\.0\.0\.1:4173[\s\S]*run: npm run build/);
  assert.match(job, /E2E_API_BASE_URL: http:\/\/127\.0\.0\.1:4173/);
});

test("credential examples are tracked while runtime secrets and artifacts are ignored", () => {
  const ignore = read(".gitignore");
  assert.match(ignore, /!\*\*\/\.env\.\*\.example/);
  for (const ignored of [
    "tests/e2e/playwright/.auth/", "tests/e2e/test-results/", "tests/load/.env.local",
    "tools/launch/.env.local", "tools/launch/generated/", "tools/telematics/captures/", "__pycache__/",
  ]) assert.ok(ignore.includes(ignored), `missing ignore ${ignored}`);
});
