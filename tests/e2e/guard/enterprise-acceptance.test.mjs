import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const repository = path.resolve(here, "../../..");
const read = (relativePath) => fs.readFileSync(path.join(repository, relativePath), "utf8");

function between(source, start, end) {
  const startAt = source.indexOf(start);
  assert.notEqual(startAt, -1, `missing source boundary: ${start}`);
  const endAt = source.indexOf(end, startAt + start.length);
  assert.notEqual(endAt, -1, `missing source boundary: ${end}`);
  return source.slice(startAt, endAt);
}

test("customer-visible control tower never claims simulated or placeholder evidence is live", () => {
  const endpoints = read("backend-dotnet/Controllers/EndpointMappings.cs");
  const summary = between(endpoints, "private static async Task<IResult> ControlTowerSummary", "private static async Task<IResult> ControlTowerEntities");
  assert.doesNotMatch(summary, /Live Simulation/i);
  assert.doesNotMatch(summary, /placeholder/i);
  assert.doesNotMatch(summary, /replay\s*=\s*new\s*\{\s*available\s*=\s*true/i);
});

test("human-entered cold-chain samples are identified as manual, never as device Sensor provenance", () => {
  const page = read("frontend/src/pages/FleetColdChainPage.tsx");
  assert.doesNotMatch(page, /source:\s*['"]Sensor['"][\s\S]{0,240}Manual telemetry sample/i);
  assert.match(page, /source:\s*['"]Manual['"]/i);
});

test("staging IoT lifecycle certifies dispatch, driver, DVIR, lineage, and delayed-alert semantics", () => {
  const lifecycle = read("tests/e2e/specs/staging.iot-lifecycle.spec.mjs");
  const requiredContracts = [
    ["dispatch assignment", /\/api\/dispatch\/assignments/],
    ["driver accepts assignment", /\/api\/driver\/assignments\/\$\{[^}]+\}\/accept/],
    ["exact vehicle confirmation", /\/confirm-vehicle/],
    ["governed pre-trip DVIR", /\/api\/driver\/dvir/],
    ["departure interlock", /en_route_pickup/],
    ["customer/job linkage", /customerId|customerName/],
    ["trip linkage", /tripId/],
    ["driver lineage", /driverId/],
    ["assignment lineage", /assignmentId/],
    ["alert baseline", /alertsBeforeDelayed|delayedAlertBaseline/],
    ["delayed event creates no current alert", /delayedEventCreatedNoCurrentAlert/],
  ];
  for (const [name, pattern] of requiredContracts) {
    assert.match(lifecycle, pattern, `missing lifecycle contract: ${name}`);
  }
});

test("staging workflow proves deep health, UI provenance, original 49 execution, and zero-skips enforcement", () => {
  const workflow = read(".github/workflows/staging-iot-certification.yml");
  for (const required of [
    "/health/deep",
    "fleet_production_contract",
    "role_restricted",
    "critical_worker_contract",
    "Require deployed UI provenance",
    "E2E_DRIVER_AUTH_STATE_B64",
    "E2E_CUSTOMER_AUTH_STATE_B64",
    "E2E_PLATFORM_AUTH_STATE_B64",
    "test:inventory",
    "original-49-results.json\" 49",
  ]) assert.ok(workflow.includes(required), `staging certification workflow is missing ${required}`);

  const executionAssertion = read("tests/e2e/scripts/assert-execution.mjs");
  assert.match(executionAssertion, /stats\.unexpected/);
  assert.match(executionAssertion, /stats\.flaky/);
  assert.match(executionAssertion, /stats\.skipped/);
  const config = read("tests/e2e/playwright.config.mjs");
  assert.match(config, /retries:\s*0/);
});
