import { test } from "../fixtures.mjs";
import { expectAuthenticatedRoute } from "./helpers.mjs";

const journeys = [
  ["/platform", /Command|Platform/i],
  ["/platform/tenants", /Tenants/i],
  ["/platform/packages", /Packages|Plans/i],
  ["/platform/billing", /Billing/i],
  ["/platform/health", /Health/i],
  ["/platform/reliability", /Reliability/i],
  ["/platform/audit", /Audit/i],
];

for (const [path, heading] of journeys) {
  test(`@readonly Platform operator can render ${path}`, async ({ page, authConfigured }) => {
    test.skip(!authConfigured, "Provide E2E_PLATFORM_AUTH_STATE for this staging persona");
    await expectAuthenticatedRoute(page, path, heading);
  });
}
