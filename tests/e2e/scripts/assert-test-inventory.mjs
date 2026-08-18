import fs from "node:fs";

const reportPath = process.argv[2];
if (!reportPath) throw new Error("Usage: node scripts/assert-test-inventory.mjs <playwright-list.json>");
const report = JSON.parse(fs.readFileSync(reportPath, "utf8"));
const actual = {};
function visit(suite) {
  for (const spec of suite.specs || []) {
    for (const test of spec.tests || []) {
      actual[test.projectName] = (actual[test.projectName] || 0) + 1;
    }
  }
  for (const child of suite.suites || []) visit(child);
}
for (const suite of report.suites || []) visit(suite);

const expected = {
  "public-chromium": 21,
  "tenant-readonly": 11,
  "driver-readonly": 6,
  "customer-readonly": 3,
  "platform-readonly": 7,
  "staging-mutations": 1,
  "staging-iot-lifecycle": 1,
};
if (JSON.stringify(actual) !== JSON.stringify(expected)) {
  throw new Error(`Playwright inventory changed. Expected ${JSON.stringify(expected)}, received ${JSON.stringify(actual)}`);
}
const total = Object.values(actual).reduce((sum, count) => sum + count, 0);
if (total !== 50) throw new Error(`Expected 50 discovered journeys after adding IoT lifecycle certification; received ${total}`);
process.stdout.write(`Verified Playwright discovery inventory: ${total} journeys; staging-iot-lifecycle=1. Discovery is not execution.\n`);
