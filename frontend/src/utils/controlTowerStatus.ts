export type ControlTowerStatusInput = {
  highRiskUnits: unknown;
  alertCount: unknown;
  actionCount: unknown;
  alertsAvailable: boolean;
};

export type ControlTowerStatusSummary = {
  evidenceIncomplete: boolean;
  isNominal: boolean;
  isCritical: boolean;
  label: string;
  details: string;
};

function nonNegativeCount(value: unknown): number | null {
  if (typeof value === "number") return Number.isSafeInteger(value) && value >= 0 ? value : null;
  if (typeof value !== "string" || !/^(0|[1-9]\d*)$/.test(value)) return null;
  const number = Number(value);
  return Number.isSafeInteger(number) ? number : null;
}

/**
 * Describe only the exception evidence actually checked by the control tower.
 * Missing alert/risk evidence can never collapse into a nominal state.
 */
export function summarizeControlTowerStatus(input: ControlTowerStatusInput): ControlTowerStatusSummary {
  const highRisk = nonNegativeCount(input.highRiskUnits);
  const parsedAlertCount = nonNegativeCount(input.alertCount);
  const parsedActionCount = nonNegativeCount(input.actionCount);
  const alertCount = parsedAlertCount ?? 0;
  const actionCount = parsedActionCount ?? 0;
  const evidenceIncomplete = highRisk == null || parsedAlertCount == null || parsedActionCount == null || !input.alertsAvailable;
  const isNominal = !evidenceIncomplete && highRisk === 0 && alertCount === 0 && actionCount === 0;
  const isCritical = !evidenceIncomplete && (highRisk! > 3 || alertCount > 5);

  if (evidenceIncomplete) {
    const details = [
      highRisk == null && "High-risk KPI unavailable",
      (!input.alertsAvailable || parsedAlertCount == null) && "Open-alert evidence unavailable",
      parsedActionCount == null && "Action-queue evidence unavailable",
      input.alertsAvailable && alertCount > 0 && `${alertCount} open alert${alertCount === 1 ? "" : "s"}`,
      actionCount > 0 && `${actionCount} queued action${actionCount === 1 ? "" : "s"}`,
    ].filter(Boolean).join(" · ");
    return { evidenceIncomplete, isNominal, isCritical, label: "Exception Evidence Incomplete", details };
  }

  if (isNominal) {
    return {
      evidenceIncomplete,
      isNominal,
      isCritical,
      label: "No Current Exceptions Reported",
      details: "No high-risk units, open telemetry alerts, or queued actions in the current authorized scope.",
    };
  }

  const details = [
    alertCount > 0 && `${alertCount} open alert${alertCount === 1 ? "" : "s"}`,
    actionCount > 0 && `${actionCount} queued action${actionCount === 1 ? "" : "s"}`,
    highRisk > 0 && `${highRisk} high-risk unit${highRisk === 1 ? "" : "s"}`,
  ].filter(Boolean).join(" · ");
  return {
    evidenceIncomplete,
    isNominal,
    isCritical,
    label: isCritical ? "Action Required" : "Review Needed",
    details,
  };
}
