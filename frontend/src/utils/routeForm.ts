export type RouteFormValues = Record<string, unknown>;

export type PreparedRouteForm = {
  payload: RouteFormValues;
  errors: string[];
};

const allowedStatuses = new Set(["Planned", "Active", "Delayed", "At Risk", "Completed", "Cancelled"]);

function text(value: unknown): string {
  return typeof value === "string" ? value.trim() : String(value ?? "").trim();
}

/** Normalizes the customer-entered route form and mirrors server validation. */
export function prepareRouteForm(values: RouteFormValues): PreparedRouteForm {
  const payload: RouteFormValues = { ...values };
  const errors: string[] = [];
  const routeCode = text(values.routeCode);
  const routeName = text(values.routeName ?? values.name);
  const plannedStart = text(values.plannedStart);
  const plannedEnd = text(values.plannedEnd);
  const status = text(values.status) || "Planned";
  const notes = text(values.notes);

  if (!routeCode) errors.push("Route code is required.");
  else if (routeCode.length > 60) errors.push("Route code cannot exceed 60 characters.");
  if (!routeName) errors.push("Route name is required.");
  else if (routeName.length > 180) errors.push("Route name cannot exceed 180 characters.");
  if (!plannedStart) errors.push("Planned start is required.");
  if (!plannedEnd) errors.push("Planned end is required.");

  const startMs = Date.parse(plannedStart);
  const endMs = Date.parse(plannedEnd);
  if (plannedStart && Number.isNaN(startMs)) errors.push("Planned start must be a valid date and time.");
  if (plannedEnd && Number.isNaN(endMs)) errors.push("Planned end must be a valid date and time.");
  if (!Number.isNaN(startMs) && !Number.isNaN(endMs) && endMs <= startMs) {
    errors.push("Planned window end must be after planned window start.");
  }
  if (!allowedStatuses.has(status)) errors.push("Route status is invalid.");
  if (notes.length > 4_000) errors.push("Route notes cannot exceed 4000 characters.");

  const costText = text(values.costEstimate);
  if (costText) {
    const cost = Number(costText);
    if (!Number.isFinite(cost) || cost < 0) errors.push("Cost estimate must be a non-negative number.");
    else payload.costEstimate = cost;
  } else {
    delete payload.costEstimate;
  }

  payload.routeCode = routeCode;
  payload.routeName = routeName;
  payload.plannedStart = plannedStart;
  payload.plannedEnd = plannedEnd;
  payload.status = status;
  for (const key of ["region", "routeType", "optimizationMode", "notes"] as const) {
    const normalized = text(values[key]);
    if (normalized) payload[key] = normalized;
    else delete payload[key];
  }

  return { payload, errors };
}
