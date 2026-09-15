import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

const jobs = readFileSync(new URL("../src/pages/JobsPage.tsx", import.meta.url), "utf8");
const jobsApi = readFileSync(new URL("../src/services/jobsApi.ts", import.meta.url), "utf8");
const safety = readFileSync(new URL("../src/pages/Batch4SafetyPage.tsx", import.meta.url), "utf8");
const tenant = readFileSync(new URL("../src/pages/platform/PlatformTenantsPage.tsx", import.meta.url), "utf8");

assert.doesNotMatch(jobs, /Queue POD|POD workflow queued|proofPlaceholder/);
assert.doesNotMatch(jobsApi, /proofPlaceholder/);
assert.match(jobs, /Capture Proof/);
assert.match(safety, /cannot be locked until every included item passes retrieval, content-hash and provenance verification/);
assert.doesNotMatch(safety, /actions:\s*\["lock"\]/);
assert.doesNotMatch(safety, /\["status","Status"\]/);
assert.match(tenant, /"tenant admin", "company admin"/);
assert.match(tenant, /roleName: "Tenant Admin"/);

console.log("POC commercial-truth UI boundaries: PASS");
