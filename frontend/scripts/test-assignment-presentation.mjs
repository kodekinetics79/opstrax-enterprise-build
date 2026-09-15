import assert from "node:assert/strict";
import {
  assignmentStage,
  currentPairings,
  nextAssignmentStatuses,
  isTerminalAssignment,
} from "../src/utils/assignmentPresentation.ts";
assert.equal(assignmentStage("arrived_delivery"), "In transit");
assert.equal(assignmentStage("delivered"), "Delivered");
assert.equal(isTerminalAssignment("arrived_delivery"), false);
assert.equal(isTerminalAssignment("cancelled"), true);
const histories = [
  { id: 4, vehicleId: 1, driverId: 2, isCurrent: true },
  { id: 3, vehicleId: 1, driverId: 2, isCurrent: true },
  { id: 2, vehicleId: 3, driverId: 4, isCurrent: false, status: "Active" },
  { id: 1, vehicle_id: 5, driver_id: 6, is_current: true },
];
assert.deepEqual(
  currentPairings(histories).map((row) => row.id),
  [4, 1],
);
assert.equal(currentPairings([{ status: "Active" }]).length, 0);
assert.deepEqual(nextAssignmentStatuses({ assignmentStatus: "assigned" }), [
  "accepted",
  "cancelled",
]);
assert.deepEqual(
  nextAssignmentStatuses({
    assignmentStatus: "exception",
    previousStatus: "in_transit",
  }),
  ["in_transit", "cancelled"],
);
assert.deepEqual(
  nextAssignmentStatuses({ assignmentStatus: "arrived_delivery" }),
  [],
);
assert.deepEqual(nextAssignmentStatuses({ assignmentStatus: "cancelled" }), []);
assert.deepEqual(
  nextAssignmentStatuses({
    assignmentStatus: "exception",
    previousStatus: "delivered",
  }),
  ["cancelled"],
);
console.log("Assignment presentation: 11 checks passed.");
