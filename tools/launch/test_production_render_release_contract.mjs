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