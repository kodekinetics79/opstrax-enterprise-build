import assert from "node:assert/strict";
import { isPodCaptureActionVisible, isPodCaptureReady, podCaptureBlockedReason } from "../src/utils/proofOfDeliveryLifecycle.ts";

for (const jobStatus of ["At Stop", "Completed", "Delivered", "at_stop", "AT-STOP"]) {
  assert.equal(isPodCaptureReady({ jobStatus, status: "Awaiting Capture" }), true, `${jobStatus} should allow POD capture`);
}

for (const jobStatus of ["Unassigned", "Assigned", "En Route", "Cancelled", ""]) {
  const row = { jobStatus, status: "Awaiting Capture" };
  assert.equal(isPodCaptureActionVisible(row), true, `${jobStatus} should keep the POD state visible`);
  assert.equal(isPodCaptureReady(row), false, `${jobStatus} must not open POD capture`);
  assert.match(podCaptureBlockedReason(row) ?? "", /At Stop, Completed, or Delivered/);
}

assert.equal(isPodCaptureActionVisible({ jobStatus: "Delivered", status: "Captured" }), false);
assert.equal(isPodCaptureReady({ jobStatus: "Delivered", status: "Captured" }), false);
assert.equal(podCaptureBlockedReason({ jobStatus: "Delivered", status: "Captured" }), undefined);

console.log("Proof-of-delivery lifecycle guard passed.");
