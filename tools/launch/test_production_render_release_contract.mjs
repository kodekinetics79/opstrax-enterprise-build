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

test("production release applies owner migrations before exact-SHA deploy", () => {
  const workflow = read(".github", "workflows", "production-render-release.yml");
  const migration = workflow.indexOf("./tools/apply-neon-predeploy-migrations.sh");
  const deploy = workflow.indexOf("node tools/render-deploy-exact.mjs");
  assert.ok(migration >= 0, "owner migration runner is missing");
  assert.ok(deploy > migration, "Render deploy must follow the successful migration chain");
  assert.match(workflow, /environment:\s*production/);
  assert.match(workflow, /ref:\s*\$\{\{ inputs\.candidate_sha \}\}/);
  assert.match(workflow, /NEON_PRODUCTION_OWNER_URI/);
  assert.match(workflow, /RENDER_API_KEY/);
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
