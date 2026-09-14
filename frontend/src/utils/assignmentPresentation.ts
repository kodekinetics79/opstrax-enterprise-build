import type { AnyRecord } from "@/types";

export const isTerminalAssignment = (status: string) =>
  ["delivered", "cancelled", "rejected", "completed"].includes(status);
export function assignmentStage(status: string) {
  if (isTerminalAssignment(status))
    return status === "cancelled" || status === "rejected"
      ? "Cancelled"
      : "Delivered";
  if (
    [
      "en_route_pickup",
      "arrived_pickup",
      "loaded",
      "in_transit",
      "arrived_delivery",
    ].includes(status)
  )
    return "In transit";
  if (status === "accepted") return "Accepted";
  if (status === "exception") return "Exception";
  return "Assigned";
}
export function currentPairings(rows: AnyRecord[]) {
  const unique = new Map<string, AnyRecord>();
  for (const row of rows) {
    if ((row.isCurrent ?? row.is_current) !== true) continue;
    const key = `${row.vehicleId ?? row.vehicle_id}:${row.driverId ?? row.driver_id}`;
    if (!unique.has(key)) unique.set(key, row);
  }
  return [...unique.values()];
}
export function nextAssignmentStatuses(row: AnyRecord): string[] {
  const status = String(
    row.assignmentStatus ?? row.assignment_status ?? row.status ?? "",
  )
    .toLowerCase()
    .replaceAll(" ", "_");
  const next: Record<string, string[]> = {
    assigned: ["accepted", "cancelled"],
    accepted: ["en_route_pickup", "cancelled"],
    en_route_pickup: ["arrived_pickup"],
    arrived_pickup: ["loaded"],
    loaded: ["in_transit"],
    in_transit: ["arrived_delivery"],
  };
  if (status === "exception") {
    const previous = String(row.previousStatus ?? row.previous_status ?? "");
    return [
      ...([
        "assigned",
        "accepted",
        "en_route_pickup",
        "arrived_pickup",
        "loaded",
        "in_transit",
        "arrived_delivery",
      ].includes(previous)
        ? [previous]
        : []),
      "cancelled",
    ];
  }
  return next[status] ?? [];
}
