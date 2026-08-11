import { test } from "../fixtures.mjs";
import { expectAuthenticatedRoute } from "./helpers.mjs";

const journeys = [
  ["/customer-portal", /Customer/i],
  ["/customer-visibility", /Customer|Visibility/i],
  ["/customer-eta", /Customer ETA/i],
];

for (const [path, heading] of journeys) {
  test(`@readonly customer can render ${path}`, async ({ page, authConfigured }) => {
    test.skip(!authConfigured, "Provide E2E_CUSTOMER_AUTH_STATE for this staging persona");
    await expectAuthenticatedRoute(page, path, heading);
  });
}
