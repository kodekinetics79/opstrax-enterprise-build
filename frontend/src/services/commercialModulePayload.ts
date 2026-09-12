export type CommercialModuleRecord = Record<string, unknown>;

function isRecord(value: unknown): value is CommercialModuleRecord {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

/**
 * Dedicated commercial-register routes use the standard module payload envelope.
 * Fail closed when that contract drifts: treating a malformed response as [] would
 * present false zero pipeline, campaign, or quotation totals to the operator.
 */
export function requireCommercialModuleRecords(payload: unknown, moduleKey: string): CommercialModuleRecord[] {
  if (!isRecord(payload) || payload.moduleKey !== moduleKey || !Array.isArray(payload.records)) {
    throw new Error(`${moduleKey} response did not include the expected records payload.`);
  }
  if (!payload.records.every(isRecord)) {
    throw new Error(`${moduleKey} response contained an invalid record.`);
  }
  return payload.records;
}
