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

test("clean chain and production rehearsal require Stage76 evidence", () => {
  const clean = read("tools/test-predeploy-clean-chain.sh");
  const rehearsal = read("tools/test-production-shaped-local-rehearsal.sh");
  assert.match(clean, /2026_08_11_stage76_telematics_security_hardening/);
  assert.match(clean, /Stage76-terminal runner replays/);
  assert.match(rehearsal, /migration_ledgers=15/);
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

test("credential examples are tracked while runtime secrets and artifacts are ignored", () => {
  const ignore = read(".gitignore");
  assert.match(ignore, /!\*\*\/\.env\.\*\.example/);
  for (const ignored of [
    "tests/e2e/playwright/.auth/", "tests/e2e/test-results/", "tests/load/.env.local",
    "tools/launch/.env.local", "tools/launch/generated/", "tools/telematics/captures/", "__pycache__/",
  ]) assert.ok(ignore.includes(ignored), `missing ignore ${ignored}`);
});
