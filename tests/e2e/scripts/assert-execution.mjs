import fs from "node:fs";

const reportPath = process.argv[2];
const expectedCount = Number(process.argv[3] || "1");
if (!reportPath) throw new Error("Usage: node scripts/assert-execution.mjs <playwright-results.json> [expected-count]");
const report = JSON.parse(fs.readFileSync(reportPath, "utf8"));
const stats = report.stats || {};
const executed = Number(stats.expected || 0) + Number(stats.unexpected || 0) + Number(stats.flaky || 0);
if (executed !== expectedCount) {
  throw new Error(`Expected ${expectedCount} executed Playwright test(s), received ${executed}; discovery/skips do not count`);
}
if (Number(stats.expected || 0) !== expectedCount || Number(stats.unexpected || 0) !== 0 || Number(stats.flaky || 0) !== 0) {
  throw new Error(`Execution was not clean: ${JSON.stringify(stats)}`);
}
if (Number(stats.skipped || 0) !== 0) {
  throw new Error(`Certification cannot contain skipped tests: ${stats.skipped} skipped`);
}
process.stdout.write(`Verified real Playwright execution: ${expectedCount}/${expectedCount} passed, 0 skipped.\n`);
