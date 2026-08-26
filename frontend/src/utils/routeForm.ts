export type RouteFormValues = Record<string, unknown>;

export type PreparedRouteForm = {
  payload: RouteFormValues;
  errors: string[];
};

const allowedStatuses = new Set(["Planned", "Active", "Delayed", "At Risk", "Completed", "Cancelled"]);
const localDateTimePattern = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})(?::(\d{2})(?:\.(\d{1,3}))?)?$/;

function text(value: unknown): string {
  return typeof value === "string" ? value.trim() : String(value ?? "").trim();
}

function pad(value: number, length = 2): string {
  return String(value).padStart(length, "0");
}

/** Converts an API instant into the wall-clock value required by datetime-local. */
export function instantToLocalDateTime(value: unknown): string {
  const raw = text(value);
  if (!raw) return "";
  const instant = new Date(raw);
  if (Number.isNaN(instant.getTime())) return raw;
  return `${pad(instant.getFullYear(), 4)}-${pad(instant.getMonth() + 1)}-${pad(instant.getDate())}`
    + `T${pad(instant.getHours())}:${pad(instant.getMinutes())}:${pad(instant.getSeconds())}`;
}

/** Interprets a datetime-local wall clock in the browser timezone and emits a UTC instant. */
export function localDateTimeToIso(value: unknown): string | null {
  const raw = text(value);
  const match = localDateTimePattern.exec(raw);
  if (!match) {
    const explicitInstant = new Date(raw);
    return raw && !Number.isNaN(explicitInstant.getTime()) ? explicitInstant.toISOString() : null;
  }
  const [, yearRaw, monthRaw, dayRaw, hourRaw, minuteRaw, secondRaw = "0", millisRaw = "0"] = match;
  const year = Number(yearRaw);
  const month = Number(monthRaw) - 1;
  const day = Number(dayRaw);
  const hour = Number(hourRaw);
  const minute = Number(minuteRaw);
  const second = Number(secondRaw);
  const millis = Number(millisRaw.padEnd(3, "0"));
  const local = new Date(year, month, day, hour, minute, second, millis);
  if (local.getFullYear() !== year || local.getMonth() !== month || local.getDate() !== day
    || local.getHours() !== hour || local.getMinutes() !== minute || local.getSeconds() !== second) return null;
  return local.toISOString();
}

export function routeFormForDisplay(values: RouteFormValues): RouteFormValues {
  return {
    ...values,
    plannedStart: instantToLocalDateTime(values.plannedStart),
    plannedEnd: instantToLocalDateTime(values.plannedEnd),
  };
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

  const startIso = plannedStart ? localDateTimeToIso(plannedStart) : null;
  const endIso = plannedEnd ? localDateTimeToIso(plannedEnd) : null;
  if (plannedStart && !startIso) errors.push("Planned start must be a valid local date and time.");
  if (plannedEnd && !endIso) errors.push("Planned end must be a valid local date and time.");
  if (startIso && endIso && Date.parse(endIso) <= Date.parse(startIso)) {
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
  payload.plannedStart = startIso ?? plannedStart;
  payload.plannedEnd = endIso ?? plannedEnd;
  payload.status = status;
  // Assignment is governed by the dedicated route assignment action. Detail
  // projections include these fields, but ordinary route edits must not replay
  // them into UpdateRoute and trigger (or bypass) assignment mutation checks.
  delete payload.assignedDriverId;
  delete payload.assignedVehicleId;
  for (const key of ["region", "routeType", "optimizationMode", "notes"] as const) {
    const normalized = text(values[key]);
    if (normalized) payload[key] = normalized;
    else delete payload[key];
  }

  return { payload, errors };
}
