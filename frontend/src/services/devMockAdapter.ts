/**
 * DEV MOCK ADAPTER — intercepts all axios requests and returns seed data
 * so the frontend is fully navigable without a running backend.
 *
 * Toggle: set DEV_MOCK_ENABLED = false in apiClient.ts to restore real API calls.
 */
import type { AxiosResponse, InternalAxiosRequestConfig } from "axios";
import {
  developmentFleetSeedData,
  getDevelopmentDashboardSummary,
  getDispatchBoardData,
  getDevelopmentAvailableDrivers,
  getDevelopmentAvailableVehicles,
  getControlTowerData,
  getFleetHealthSummary,
  getFleetHealthRisks,
  getMaintenanceDashboard,
  getRoutesListData,
  getRoutesSummary,
  getFuelSummary,
  getFuelTransactions,
  getExpensesSummary,
  getExpensesList,
  getSafetySummary,
  getSafetyEvents,
  getDashcamSummary,
  getDashcamEvents,
  getCoachingSummary,
  getCoachingTasks,
  getHosDriverClocks,
  getReportDatasets,
  getAuditLogs,
  findDevelopmentVehicle,
  findDevelopmentDriver,
  findDevelopmentShipment,
  findDevelopmentCustomer,
  findDevelopmentMaintenanceRecord,
  findDevelopmentSafetyIncident,
} from "@/data/developmentFleetSeedData";
import type { AnyRecord } from "@/types";

// ── Helpers ──────────────────────────────────────────────────────────────────

function envelope<T>(data: T) {
  return { success: true, data, message: "", errors: [] };
}

/** For endpoints consumed via `const { data } = await apiClient.get<{ data: T }>(...)` */
function directWrap<T>(data: T) {
  return { data };
}

function ok(body: AnyRecord = {}) {
  return envelope({ ok: true, ...body });
}

function matchUrl(url: string, pattern: RegExp): RegExpMatchArray | null {
  return url.match(pattern);
}

// ── Route table ──────────────────────────────────────────────────────────────

function resolveGet(url: string): unknown | undefined {
  // ── Command center / dashboard ──
  if (/\/api\/command-center\/summary/.test(url)) return envelope(getDevelopmentDashboardSummary());

  // ── Vehicles ──
  if (/\/api\/vehicles\/planning-insights/.test(url)) return envelope({
    capitalPlanning: [
      { vehicleCode: "KSA-REEFER-214", recommendation: "Continue service cadence", lifecycleCost: 42000, replacementYear: 2029 },
      { vehicleCode: "BOX-106", recommendation: "Schedule replacement evaluation", lifecycleCost: 89000, replacementYear: 2026 },
    ],
  });
  {
    const m = matchUrl(url, /\/api\/vehicles\/([^/?]+)$/);
    if (m) {
      const v = findDevelopmentVehicle(m[1]);
      if (v) return envelope({
        record: v, maintenance: developmentFleetSeedData.maintenanceRecords.filter((r) => String(r.vehicleCode) === String((v as AnyRecord).vehicleCode ?? (v as AnyRecord).vehicleId)),
        compliance: developmentFleetSeedData.complianceRecords.filter((r) => r.scope === "Vehicle" && String(r.entityId) === String((v as AnyRecord).vehicleId)),
        documents: [], safetyEvents: [], trips: [], timeline: [{ eventType: "status.update", title: "Vehicle record retrieved", severity: "Low", eventTime: new Date().toISOString() }],
        recommendations: [{ id: `rec-${m[1]}`, title: "Review vehicle readiness", body: "Maintain service cadence.", score: 86 }], auditTrail: [],
      });
    }
  }
  if (/\/api\/vehicles(\?|$)/.test(url)) return envelope(developmentFleetSeedData.vehicles);

  // ── Drivers ──
  {
    const m = matchUrl(url, /\/api\/drivers\/([^/?]+)$/);
    if (m) {
      const d = findDevelopmentDriver(m[1]);
      if (d) return envelope({
        record: d, certifications: [], documents: [], hos: [], inspections: [], safetyEvents: [],
        timeline: [{ eventType: "status.update", title: "Driver record retrieved", severity: "Low", eventTime: new Date().toISOString() }],
        auditTrail: [], recommendations: [{ id: `rec-${m[1]}`, title: "Review driver fit", body: "Match vehicle assignment.", score: 84 }],
      });
    }
  }
  if (/\/api\/drivers(\?|$)/.test(url)) return envelope(developmentFleetSeedData.drivers);

  // ── Shipments ──
  {
    const m = matchUrl(url, /\/api\/shipments\/([^/?]+)$/);
    if (m) {
      const s = findDevelopmentShipment(m[1]);
      if (s) return envelope({ record: s, stops: [], proof: [], communications: [], timeline: [], auditTrail: [], recommendations: [] });
    }
  }
  if (/\/api\/shipments(\?|$)/.test(url)) return envelope(developmentFleetSeedData.shipments);

  // ── Customers ──
  {
    const m = matchUrl(url, /\/api\/customers\/([^/?]+)$/);
    if (m) {
      const c = findDevelopmentCustomer(m[1]);
      if (c) return envelope({ record: c, contacts: [], addresses: [], activeJobs: [], communications: [], contracts: [], etaHistory: [], auditTrail: [], recommendations: [] });
    }
  }
  if (/\/api\/customers(\?|$)/.test(url)) return envelope(developmentFleetSeedData.customers);

  // ── Alerts ──
  if (/\/api\/alerts\/summary/.test(url)) return envelope({
    total: developmentFleetSeedData.alerts.length,
    open: developmentFleetSeedData.alerts.filter((a) => /Open|In Progress/i.test(String(a.status))).length,
    critical: developmentFleetSeedData.alerts.filter((a) => /Critical/i.test(String(a.severity))).length,
  });
  {
    const m = matchUrl(url, /\/api\/alerts\/([^/?]+)$/);
    if (m && !m[1].includes("summary")) {
      const a = developmentFleetSeedData.alerts.find((x) => String(x.alertId) === m[1]);
      if (a) return envelope({ record: a, timeline: [], auditTrail: [] });
    }
  }
  if (/\/api\/alerts(\?|$)/.test(url)) return envelope(developmentFleetSeedData.alerts);

  // ── Maintenance ──
  if (/\/api\/maintenance\/dashboard/.test(url)) return envelope(getMaintenanceDashboard());
  {
    const m = matchUrl(url, /\/api\/maintenance\/([^/?]+)$/);
    if (m && !m[1].includes("dashboard")) {
      const r = findDevelopmentMaintenanceRecord(m[1]);
      if (r) return envelope({ record: r, timeline: [], auditTrail: [], recommendations: [] });
    }
  }
  if (/\/api\/maintenance(\?|$)/.test(url)) return envelope(developmentFleetSeedData.maintenanceRecords);

  // ── Incidents / Safety ──
  if (/\/api\/incidents\/summary|\/api\/safety\/summary/.test(url)) return envelope(getSafetySummary());
  if (/\/api\/safety\/events/.test(url)) return envelope(getSafetyEvents());
  if (/\/api\/safety\/dashcam\/summary|\/api\/dashcam\/summary/.test(url)) return envelope(getDashcamSummary());
  if (/\/api\/safety\/dashcam|\/api\/dashcam\/events/.test(url)) return envelope(getDashcamEvents());
  if (/\/api\/safety\/coaching\/summary|\/api\/coaching\/summary/.test(url)) return envelope(getCoachingSummary());
  if (/\/api\/safety\/coaching|\/api\/coaching\/tasks/.test(url)) return envelope(getCoachingTasks());
  if (/\/api\/safety\/hos|\/api\/hos\/clocks/.test(url)) return envelope(getHosDriverClocks());
  {
    const m = matchUrl(url, /\/api\/incidents\/([^/?]+)$/);
    if (m && !m[1].includes("summary")) {
      const i = findDevelopmentSafetyIncident(m[1]);
      if (i) return envelope({ record: i, timeline: [], auditTrail: [], recommendations: [] });
    }
  }
  if (/\/api\/incidents(\?|$)/.test(url)) return envelope(developmentFleetSeedData.safetyIncidents);

  // ── Dispatch ──
  if (/\/api\/dispatch\/summary/.test(url)) return envelope({
    totalJobs: developmentFleetSeedData.jobs.length,
    unassigned: developmentFleetSeedData.jobs.filter((j) => /Unassigned/i.test(String(j.status))).length,
    assigned: developmentFleetSeedData.jobs.filter((j) => /Assigned/i.test(String(j.status))).length,
    inProgress: developmentFleetSeedData.jobs.filter((j) => /In Progress|Active/i.test(String(j.status))).length,
    completed: developmentFleetSeedData.jobs.filter((j) => /Completed|Delivered/i.test(String(j.status))).length,
    atRisk: developmentFleetSeedData.jobs.filter((j) => /At Risk/i.test(String(j.slaStatus))).length,
    availableDrivers: getDevelopmentAvailableDrivers().length,
    availableVehicles: getDevelopmentAvailableVehicles().length,
  });
  if (/\/api\/dispatch\/board/.test(url)) return envelope(getDispatchBoardData());
  if (/\/api\/dispatch\/assignments\/[^/]+\/(accept|status|exception)/.test(url)) return ok({ updated: true });
  {
    const m = matchUrl(url, /\/api\/dispatch\/assignments\/([^/?]+)$/);
    if (m) {
      const j = developmentFleetSeedData.jobs.find((x) => String(x.id) === m[1] || String(x.jobCode) === m[1]);
      if (j) return envelope({ record: j, route: null, stops: [], timeline: [] });
    }
  }
  if (/\/api\/dispatch\/assignments/.test(url)) return envelope(developmentFleetSeedData.jobs.map((j) => ({ ...j, assignmentStatus: j.status })));

  // ── Fleet Health ──
  if (/\/api\/fleet-health\/summary/.test(url)) return envelope(getFleetHealthSummary());
  if (/\/api\/fleet-health\/risks/.test(url)) return envelope(getFleetHealthRisks());
  {
    const m = matchUrl(url, /\/api\/fleet-health\/vehicles\/([^/?]+)$/);
    if (m) {
      const v = findDevelopmentVehicle(m[1]);
      return envelope({ record: v ?? {}, risks: getFleetHealthRisks().filter((r) => r.entityType === "vehicle") });
    }
  }
  {
    const m = matchUrl(url, /\/api\/fleet-health\/drivers\/([^/?]+)$/);
    if (m) {
      const d = findDevelopmentDriver(m[1]);
      return envelope({ record: d ?? {}, risks: getFleetHealthRisks().filter((r) => r.entityType === "driver") });
    }
  }

  // ── Compliance ──
  if (/\/api\/compliance\/summary/.test(url)) return envelope({
    totalRecords: developmentFleetSeedData.complianceRecords.length,
    compliant: developmentFleetSeedData.complianceRecords.filter((r) => /Compliant/i.test(String(r.status))).length,
    warnings: developmentFleetSeedData.complianceRecords.filter((r) => /Warning|Review/i.test(String(r.status))).length,
    expiringSoon: developmentFleetSeedData.complianceRecords.filter((r) => /Expiring/i.test(String(r.notes ?? ""))).length,
  });
  if (/\/api\/compliance\/violations/.test(url)) return envelope(developmentFleetSeedData.complianceRecords.filter((r) => /Warning|Review/i.test(String(r.status))));
  if (/\/api\/compliance\/documents/.test(url)) return envelope([]);

  // ── Control Tower ──
  if (/\/api\/control-tower/.test(url)) return envelope(getControlTowerData());

  // ── Routes ──
  if (/\/api\/routes\/summary/.test(url)) return envelope(getRoutesSummary());
  if (/\/api\/routes(\?|$)/.test(url)) return envelope(getRoutesListData());

  // ── Fuel ──
  if (/\/api\/fuel\/summary/.test(url)) return envelope(getFuelSummary());
  if (/\/api\/fuel\/transactions/.test(url)) return envelope(getFuelTransactions());

  // ── Expenses ──
  if (/\/api\/expenses\/summary/.test(url)) return envelope(getExpensesSummary());
  if (/\/api\/expenses(\?|$|\/list)/.test(url)) return envelope(getExpensesList());

  // ── Jobs / Bookings ──
  if (/\/api\/jobs(\?|$)/.test(url)) return envelope(developmentFleetSeedData.jobs);
  if (/\/api\/bookings(\?|$)/.test(url)) return envelope(developmentFleetSeedData.bookings);

  // ── Devices ──
  if (/\/api\/devices(\?|$)/.test(url)) return envelope(developmentFleetSeedData.devices);

  // ── Invoices ──
  if (/\/api\/invoices(\?|$)/.test(url)) return envelope(developmentFleetSeedData.invoices);

  // ── Support Tickets ──
  if (/\/api\/support-tickets(\?|$)/.test(url)) return envelope(developmentFleetSeedData.supportTickets);

  // ── Reporting (direct-wrap pattern: service reads `data.data`) ──
  if (/\/api\/reports\/datasets/.test(url)) return directWrap(getReportDatasets());
  if (/\/api\/reports\/saved\/\d+\/run/.test(url)) return directWrap({ rows: developmentFleetSeedData.shipments.slice(0, 3), meta: { total: 3, page: 1, pageSize: 50, datasetKey: "shipments", executionMs: 12 } });
  if (/\/api\/reports\/saved\/\d+\/export/.test(url)) return "col1,col2\nval1,val2";
  if (/\/api\/reports\/saved\/\d+/.test(url)) return directWrap({ id: 1, companyId: 1, ownerUserId: 1, name: "Sample Report", datasetKey: "shipments", selectedFieldsJson: "[]", visibility: "private" as const, createdAt: new Date().toISOString() });
  if (/\/api\/reports\/saved/.test(url)) return directWrap([]);
  if (/\/api\/reports\/run/.test(url)) return directWrap({ rows: [], meta: { total: 0, page: 1, pageSize: 50, datasetKey: "query", executionMs: 8 } });
  if (/\/api\/reports\/export/.test(url)) return "col1,col2\nval1,val2";
  if (/\/api\/reports\/scheduled/.test(url)) return directWrap({ id: 1 });

  // ── Analytics (direct-wrap) ──
  if (/\/api\/analytics\/trends/.test(url)) return directWrap({ weekly: [12, 18, 22, 19, 26, 31, 28] });
  if (/\/api\/analytics\/insights/.test(url)) return directWrap([
    { title: "Fleet utilization up 8%", body: "Dispatch efficiency improved week-over-week." },
    { title: "Maintenance backlog rising", body: "3 work orders overdue — schedule this week." },
  ]);
  if (/\/api\/analytics\//.test(url)) return directWrap(getDevelopmentDashboardSummary());

  // ── Admin ──
  if (/\/api\/admin\/users/.test(url)) return envelope(developmentFleetSeedData.adminUsers);
  if (/\/api\/admin\/roles/.test(url)) return envelope(developmentFleetSeedData.adminRoles);
  if (/\/api\/admin\/settings/.test(url)) return envelope(developmentFleetSeedData.adminSettings);
  if (/\/api\/admin\/audit-logs/.test(url)) return envelope(developmentFleetSeedData.adminAuditLogs);
  if (/\/api\/admin\/tenants/.test(url)) return envelope(developmentFleetSeedData.tenants);
  if (/\/api\/admin\/permissions/.test(url)) return envelope(developmentFleetSeedData.permissions);

  // ── Platform ──
  if (/\/api\/platform\/login/.test(url)) return envelope({ token: "dev-platform-token", admin: { id: 1, email: "superadmin@opstrax.com", name: "Mason Lee" }, role: { key: "super_admin", name: "Super Admin" }, permissions: ["*"] });
  if (/\/api\/platform\//.test(url)) return envelope({ tenants: developmentFleetSeedData.tenants, services: [] });

  // ── Telemetry / Ops metrics ──
  if (/\/api\/ops\/metrics|\/api\/ops\/status/.test(url)) return envelope({
    telemetry: { total: 142, accepted: 138, rejected: 2, authFailed: 2, replayDetected: 0 },
    alerts: { total24h: 8, openCount: 3, criticalCount: 1 },
    safety: { generated24h: 4, openReview: 2 },
    dispatch: { active: 12, withExceptions: 3, openExceptions: 2 },
    notifications: { pending: 5, failed: 0, acknowledged24h: 12, notConfigured: 0 },
    reports: { succeeded: 24, failed: 1, activeSchedules: 8, runs24h: 16 },
    services: [
      { serviceName: "Escalation", lastHeartbeatUtc: new Date().toISOString(), lastRunUtc: new Date().toISOString(), lastRunStatus: "OK", consecutiveFailures: 0, lastErrorSafe: null },
      { serviceName: "Maintenance", lastHeartbeatUtc: new Date().toISOString(), lastRunUtc: new Date().toISOString(), lastRunStatus: "OK", consecutiveFailures: 0, lastErrorSafe: null },
      { serviceName: "Safety", lastHeartbeatUtc: new Date().toISOString(), lastRunUtc: new Date().toISOString(), lastRunStatus: "OK", consecutiveFailures: 0, lastErrorSafe: null },
      { serviceName: "Telemetry", lastHeartbeatUtc: new Date().toISOString(), lastRunUtc: new Date().toISOString(), lastRunStatus: "OK", consecutiveFailures: 0, lastErrorSafe: null },
      { serviceName: "Trip", lastHeartbeatUtc: new Date().toISOString(), lastRunUtc: new Date().toISOString(), lastRunStatus: "OK", consecutiveFailures: 0, lastErrorSafe: null },
    ],
  });

  // ── Market packs / fleet compliance ──
  if (/\/api\/market-packs/.test(url)) return envelope({ packs: [], requirements: [] });
  if (/\/api\/fleet-compliance/.test(url)) return envelope({ records: [], summary: {} });

  // ── Fleet TMS / logistics ──
  if (/\/api\/fleet-tms\/logistics\/overview/.test(url)) return envelope({
    totalOrders: 14, inTransit: 6, delivered: 5, pending: 3, activeRoutes: 4, delayedShipments: 1, onTimeRate: 87,
  });
  if (/\/api\/fleet-tms\/logistics\/orders\/\d+/.test(url)) return envelope({ id: 1, orderNumber: "ORD-001", status: "In Transit" });
  if (/\/api\/fleet-tms\/logistics\/orders/.test(url)) return envelope({ total: 0, page: 1, pageSize: 50, items: [] });
  if (/\/api\/fleet-tms\/logistics\/routes\/\d+\/stops/.test(url)) return envelope({ items: [] });
  if (/\/api\/fleet-tms\/logistics\/routes/.test(url)) return envelope({ items: getRoutesListData() });
  if (/\/api\/fleet-tms\/logistics\/last-mile/.test(url)) return envelope({ total: 0, page: 1, pageSize: 50, items: [] });

  // ── Audit logs ──
  if (/\/api\/audit/.test(url)) return envelope(getAuditLogs());

  // ── Auth / CSRF / session ──
  if (/\/api\/auth/.test(url)) return envelope({ token: "dev-bypass-token", csrfToken: "dev-bypass-csrf" });
  if (/\/api\/csrf/.test(url)) return { csrfToken: "dev-bypass-csrf" };

  // ── About ──
  if (/\/api\/about/.test(url)) return envelope({ version: "1.0.0-dev", buildDate: new Date().toISOString(), environment: "development" });

  // ── Notifications ──
  if (/\/api\/notifications/.test(url)) return envelope([]);

  // ── Messages ──
  if (/\/api\/messages/.test(url)) return envelope([]);

  // ── Fallback ──
  return undefined;
}

// ── Adapter ──────────────────────────────────────────────────────────────────

export function createMockAdapter(realAdapter: (config: InternalAxiosRequestConfig) => Promise<AxiosResponse>) {
  return async (config: InternalAxiosRequestConfig): Promise<AxiosResponse> => {
    const method = (config.method ?? "get").toLowerCase();

    let body: unknown;

    if (method === "get") {
      body = resolveGet(config.url ?? "");
    } else if (method === "post" || method === "put" || method === "patch" || method === "delete") {
      if (/\/api\/reports\/export/.test(config.url ?? "")) {
        body = "col1,col2\nval1,val2";
      } else {
        const payload = typeof config.data === "string" ? JSON.parse(config.data || "{}") : (config.data ?? {});
        body = envelope({ ok: true, id: Math.floor(Math.random() * 90000) + 10000, ...payload });
      }
    }

    if (body === undefined) {
      body = envelope([]);
    }

    const response: AxiosResponse = {
      data: body,
      status: 200,
      statusText: "OK",
      headers: { "content-type": "application/json", "x-csrf-token": "dev-bypass-csrf" },
      config,
    };
    return response;
  };
}
