import assert from "node:assert/strict";
import fs from "node:fs";
import { resolveAuthorizedSummaryCount } from "../src/utils/vehicleSummaryPresentation.ts";

const audit = fs.readFileSync(new URL("../src/pages/AuditLogsPage.tsx", import.meta.url), "utf8");
const vehicles = fs.readFileSync(new URL("../src/pages/VehiclesPage.tsx", import.meta.url), "utf8");

assert.doesNotMatch(
  audit,
  /immutable|all system actions/i,
  "Audit UI must not claim unproven immutability or complete module coverage",
);
assert.match(
  audit,
  /Operational record of recorded system activity for internal review/,
  "Audit UI must use bounded operational-record language",
);
assert.match(
  audit,
  /do not constitute a legally certified audit trail/,
  "Audit UI must retain the legal-certification disclaimer",
);

assert.match(
  vehicles,
  /moving on page[\s\S]*available on page[\s\S]*need attention in authorized scope/,
  "Vehicle header must distinguish page-scoped and authorized-scope counts",
);
assert.match(
  vehicles,
  /label="Page readiness"[\s\S]*assessed on this page/,
  "Readiness KPI must disclose page scope",
);
assert.match(
  vehicles,
  /label="Moving on page"[\s\S]*available on this page/,
  "Movement KPI must disclose page scope",
);
assert.match(vehicles, /label="Authorized scope at risk"/, "At-risk KPI must disclose authorized scope");
assert.match(vehicles, /label="Authorized scope device \/ camera gaps"/, "Device-gap KPI must disclose authorized scope");
assert.match(vehicles, /\{moving\} moving on page/, "Vehicle footer movement count must disclose page scope");
assert.doesNotMatch(vehicles, /need attention fleet-wide|Fleet-wide high risk|Fleet-wide telematics/, "Restricted users must not see tenant-wide scope claims");

assert.equal(resolveAuthorizedSummaryCount(true, 0), 0, "A legitimate summary zero must remain zero");
assert.equal(resolveAuthorizedSummaryCount(true, "0"), 0, "A serialized summary zero must remain zero");
assert.equal(resolveAuthorizedSummaryCount(true, 51), 51, "A valid summary count must be preserved");
assert.equal(resolveAuthorizedSummaryCount(false, 51), null, "Loading or failed summaries must render unavailable");
assert.equal(resolveAuthorizedSummaryCount(true, null), null, "Absent summaries must render unavailable");
assert.equal(resolveAuthorizedSummaryCount(true, ""), null, "Blank summaries must render unavailable");
assert.equal(resolveAuthorizedSummaryCount(true, -1), null, "Invalid negative summaries must render unavailable");
assert.equal(resolveAuthorizedSummaryCount(true, 1.5), null, "Fractional counts must render unavailable");
assert.equal(resolveAuthorizedSummaryCount(true, false), null, "Boolean values must render unavailable");
assert.equal(resolveAuthorizedSummaryCount(true, " 1"), null, "Whitespace-padded counts must render unavailable");
assert.equal(resolveAuthorizedSummaryCount(true, Number.MAX_SAFE_INTEGER + 1), null, "Unsafe integer counts must render unavailable");
assert.equal(resolveAuthorizedSummaryCount(true, "not-a-number"), null, "Invalid summary values must render unavailable");

console.log("Commercial-truth copy contract passed.");
