import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { apiErrorMessage } from "../src/utils/apiErrorMessage.ts";
import { resolveJobActionAccess } from "../src/utils/jobActionAccess.ts";
import { prepareRouteForm } from "../src/utils/routeForm.ts";

const direct = (...owned) => {
  const permissions = new Set(owned);
  return (permission) => permissions.has(permission);
};
assert.deepEqual(resolveJobActionAccess(direct("shipments:view")), {
  create: false,
  import: false,
  export: false,
});
assert.deepEqual(resolveJobActionAccess(direct("shipments:create")), {
  create: true,
  import: true,
  export: false,
});
assert.deepEqual(resolveJobActionAccess(direct("shipments:export")), {
  create: false,
  import: false,
  export: true,
});
assert.deepEqual(resolveJobActionAccess(direct("dispatch:manage")), {
  create: true,
  import: true,
  export: false,
});

const valid = prepareRouteForm({
  routeCode: "  RT-100  ",
  routeName: "  Customer delivery  ",
  plannedStart: "2026-08-27T09:00",
  plannedEnd: "2026-08-27T11:00",
  status: "Planned",
  costEstimate: "125.50",
});
assert.deepEqual(valid.errors, []);
assert.equal(valid.payload.routeCode, "RT-100");
assert.equal(valid.payload.routeName, "Customer delivery");
assert.equal(valid.payload.costEstimate, 125.5);

const invalid = prepareRouteForm({
  routeCode: "RT-101",
  routeName: "Invalid window",
  plannedStart: "2026-08-27T12:00",
  plannedEnd: "2026-08-27T10:00",
  costEstimate: "-1",
});
assert.ok(invalid.errors.includes("Planned window end must be after planned window start."));
assert.ok(invalid.errors.includes("Cost estimate must be a non-negative number."));

const handled = apiErrorMessage({
  response: { data: { message: "Route validation failed", errors: ["Route code must be unique."] } },
}, "fallback");
assert.equal(handled, "Route validation failed: Route code must be unique.");
assert.equal(apiErrorMessage(new Error("Request failed with status code 400"), "Safe fallback"), "Safe fallback");

const root = resolve(fileURLToPath(new URL("..", import.meta.url)));
const jobsPage = readFileSync(resolve(root, "src/pages/JobsPage.tsx"), "utf8");
const routePage = readFileSync(resolve(root, "src/pages/RoutePlanningPage.tsx"), "utf8");
assert.match(jobsPage, /<select[\s\S]*id="job-customer-selector"[\s\S]*Select an active customer/);
assert.doesNotMatch(jobsPage, /\["customerId",\s*"Customer ID"\]/);
assert.match(jobsPage, /No active customers are available/);
assert.match(jobsPage, /useHasDirectPermission/);
assert.match(jobsPage, /canCreate \? <button[\s\S]*Create Job/);
assert.match(jobsPage, /canImport \? <>[\s\S]*Import CSV/);
assert.match(jobsPage, /canExport \? <button[\s\S]*Export Roster/);
assert.match(routePage, /prepareRouteForm\(form\)/);
assert.match(routePage, /id="route-form-errors" role="alert"/);
assert.match(routePage, /serverError=\{save\.error \? apiErrorMessage/);

console.log("Live Operations create-journey behavior contract passed.");
