import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const read = (name) => fs.readFileSync(path.join(repository, name), "utf8");

test("the primary navigation exposes the complete live telematics workspace", () => {
  const shell = read("frontend/src/layouts/AppShell.tsx");
  const telematicsGroup = shell.slice(shell.indexOf('label: "Telematics"'), shell.indexOf('label: "Customers"'));
  for (const moduleKey of [
    "telematics-control-tower",
    "iot-devices",
    "gps-tracking",
    "obd-j1939",
    "sensor-health",
    "cold-chain",
  ]) {
    assert.ok(telematicsGroup.includes(`"${moduleKey}"`), `Telematics navigation hides ${moduleKey}`);
  }
  assert.ok(
    telematicsGroup.indexOf('"telematics-control-tower"') < telematicsGroup.indexOf('"iot-devices"'),
    "Control Tower must be the first telematics destination",
  );
});

test("cold-chain telematics consumes the dedicated live service contract", () => {
  const page = read("frontend/src/pages/TelematicsCommandPage.tsx");
  const service = read("frontend/src/services/telematicsService.ts");
  const modules = read("frontend/src/modules/moduleConfig.ts");
  assert.match(page, /"cold-chain":[\s\S]*query: \(\) => telematicsService\.getColdChainRecords\(\)/);
  assert.match(service, /fleetColdChainApi\.devices\(\)/);
  assert.match(service, /fleetColdChainApi\.alerts\(\)/);
  assert.match(service, /fleetColdChainApi\.summary\(\)/);
  assert.match(modules, /key: "cold-chain"[\s\S]*requiredPermission: "fleet:view"/);
});

test("telematics does not manufacture a tenant-one identity or import mock data", () => {
  const service = read("frontend/src/services/telematicsService.ts");
  assert.doesNotMatch(service, /company_id\s*\?\?\s*1|companyId\s*\?\?\s*1|tenantId\s*\?\?\s*1/);
  assert.doesNotMatch(service, /@\/data\/mock|mockOperatingData/);
  assert.match(service, /return Number\.isFinite\(parsed\) && parsed > 0 \? parsed : 0/);
});
