import { test } from "../fixtures.mjs";
import { expectAuthenticatedRoute } from "./helpers.mjs";

const journeys = [
  ["/live-dashboard", /Fleet Command/i],
  ["/map-view", /Live Map/i],
  ["/vehicles/overview", /Vehicles/i],
  ["/drivers/overview", /Drivers/i],
  ["/dispatch", /Dispatch/i],
  ["/trips", /Trips/i],
  ["/work-orders", /Work Orders/i],
  ["/proof-of-delivery", /Proof of Delivery/i],
  ["/safety", /Safety/i],
  ["/reports", /Reports/i],
  ["/user-management", /Users|Administration/i],
];

for (const [path, heading] of journeys) {
  test(`@readonly tenant can render ${path}`, async ({ page, authConfigured }) => {
    test.skip(!authConfigured, "Provide E2E_TENANT_AUTH_STATE for this staging persona");
    await expectAuthenticatedRoute(page, path, heading);
  });
}
