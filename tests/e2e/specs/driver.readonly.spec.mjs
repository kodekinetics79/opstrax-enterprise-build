import { test } from "../fixtures.mjs";
import { expectAuthenticatedRoute } from "./helpers.mjs";

const journeys = [
  ["/driver", /Driver/i],
  ["/driver/assignments", /Assignments/i],
  ["/driver/dvir", /DVIR|Inspection/i],
  ["/driver/coaching", /Coaching/i],
  ["/driver/hos", /Hours|HOS/i],
  ["/driver/messages", /Messages/i],
];

for (const [path, heading] of journeys) {
  test(`@readonly driver can render ${path}`, async ({ page, authConfigured }) => {
    test.skip(!authConfigured, "Provide E2E_DRIVER_AUTH_STATE for this staging persona");
    await expectAuthenticatedRoute(page, path, heading);
  });
}
