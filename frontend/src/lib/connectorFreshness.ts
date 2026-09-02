export type ConnectorFreshnessInput = {
  key: string;
  status: "Connected" | "Disconnected" | "Pending" | "Error";
  syncLastAttemptAt?: string | null;
  syncLastCompletedAt?: string | null;
  syncLastOk?: boolean | null;
  providerLastEventAt?: string | null;
};

export type ConnectorFreshness = {
  state: "awaiting" | "in-progress" | "fresh" | "stale" | "error";
  label: string;
  detail: string;
  announcement: string;
  tone: string;
};

export const CONNECTOR_STALE_AFTER_MS = 15 * 60 * 1000;
export const CONNECTOR_OPERATION_OVERDUE_MS = 90 * 1000;

function relativeTime(iso: string, nowMs: number): string {
  const then = new Date(iso).getTime();
  if (Number.isNaN(then)) return "at an unknown time";
  const diffSec = Math.max(0, Math.round((nowMs - then) / 1000));
  if (diffSec < 45) return "just now";
  if (diffSec < 3600) return `${Math.round(diffSec / 60)}m ago`;
  if (diffSec < 86400) return `${Math.round(diffSec / 3600)}h ago`;
  return `${Math.round(diffSec / 86400)}d ago`;
}

export function connectorAttemptHealth(
  integration: ConnectorFreshnessInput,
  nowMs = Date.now(),
): ConnectorFreshness | null {
  if (integration.key !== "samsara") return null;
  if (integration.status === "Disconnected" || integration.status === "Pending") return null;

  const attemptedAt = integration.syncLastAttemptAt;
  if (!attemptedAt) {
    return {
      state: "awaiting",
      label: "Awaiting first sync attempt",
      detail: "No polling or manual-sync attempt has been recorded.",
      announcement: "Connector sync status: awaiting first sync attempt.",
      tone: "border-amber-200/70 bg-amber-50 text-amber-800",
    };
  }

  const attemptedMs = new Date(attemptedAt).getTime();
  const completedMs = integration.syncLastCompletedAt
    ? new Date(integration.syncLastCompletedAt).getTime()
    : Number.NaN;
  const attemptAgeMs = Number.isNaN(attemptedMs) ? Number.POSITIVE_INFINITY : nowMs - attemptedMs;
  const hasTerminalResult = !Number.isNaN(completedMs) && completedMs >= attemptedMs;

  if (!hasTerminalResult) {
    const overdue = attemptAgeMs > CONNECTOR_OPERATION_OVERDUE_MS;
    return overdue
      ? {
          state: "error",
          label: `Sync attempt overdue ${relativeTime(attemptedAt, nowMs)}`,
          detail: "The sync attempt has no terminal result and exceeded its 90-second lease ceiling.",
          announcement: "Connector sync status: attempt overdue.",
          tone: "border-red-200/70 bg-red-50 text-red-800",
        }
      : {
          state: "in-progress",
          label: `Sync in progress · started ${relativeTime(attemptedAt, nowMs)}`,
          detail: "A polling or manual data-sync attempt is in progress.",
          announcement: "Connector sync status: sync in progress.",
          tone: "border-sky-200/70 bg-sky-50 text-sky-800",
        };
  }

  if (integration.syncLastOk === false) {
    return {
      state: "error",
      label: `Last sync attempt failed ${relativeTime(integration.syncLastCompletedAt!, nowMs)}`,
      detail: "The latest data-sync attempt failed. Review the result and reconnect or retry.",
      announcement: "Connector sync status: latest sync attempt failed.",
      tone: "border-red-200/70 bg-red-50 text-red-800",
    };
  }

  if (attemptAgeMs > CONNECTOR_STALE_AFTER_MS || Number.isNaN(attemptedMs)) {
    return {
      state: "stale",
      label: `Sync attempt stale ${relativeTime(attemptedAt, nowMs)}`,
      detail: "No data-sync attempt has been recorded within the 15-minute pilot freshness threshold.",
      announcement: "Connector sync status: polling attempt is stale.",
      tone: "border-amber-200/70 bg-amber-50 text-amber-800",
    };
  }

  const providerEventAt = integration.providerLastEventAt;
  if (!providerEventAt) {
    return {
      state: "awaiting",
      label: "Sync succeeded; awaiting provider telemetry",
      detail: "The connector ran successfully, but no authentic provider event has been recorded yet.",
      announcement: "Connector sync status: sync succeeded; awaiting provider telemetry.",
      tone: "border-amber-200/70 bg-amber-50 text-amber-800",
    };
  }

  const providerEventMs = new Date(providerEventAt).getTime();
  const providerEventAgeMs = Number.isNaN(providerEventMs)
    ? Number.POSITIVE_INFINITY
    : nowMs - providerEventMs;
  if (providerEventAgeMs > CONNECTOR_STALE_AFTER_MS) {
    return {
      state: "stale",
      label: `Provider telemetry stale ${relativeTime(providerEventAt, nowMs)}`,
      detail: "The connector is polling, but its newest authentic provider event exceeds the 15-minute pilot threshold.",
      announcement: "Connector sync status: provider telemetry is stale.",
      tone: "border-amber-200/70 bg-amber-50 text-amber-800",
    };
  }

  return {
    state: "fresh",
    label: `Latest sync succeeded · newest provider event ${relativeTime(providerEventAt, nowMs)}`,
    detail: "The latest sync attempt succeeded and the newest authentic provider event is within 15 minutes. This sentinel is not the p95/p99 certification distribution.",
    announcement: "Connector sync status: latest sync succeeded and the newest provider event is within the pilot threshold.",
    tone: "border-emerald-200/70 bg-emerald-50 text-emerald-800",
  };
}
