import type { AnyRecord } from "@/types";

export const DOCUMENT_METADATA_FIELDS = [
  "title", "documentNumber", "entityType", "entityId", "documentType", "category",
  "countryCode", "issuingAuthority", "issuedAt", "expiresAt", "notes",
] as const;
export const DOCUMENT_STATUSES = ["Active", "Expiring", "Expired", "Unknown"] as const;
export const DOCUMENT_RENEWAL_STATUSES = ["Current", "Renewal Required", "Renewal Queued", "Unknown"] as const;
export type DocumentIntent = "preserve" | "automatic" | "manual";

const asRecord = (value: unknown): AnyRecord =>
  value !== null && typeof value === "object" && !Array.isArray(value) ? value as AnyRecord : {};

export function documentVersion(value: unknown): string {
  if (typeof value !== "string" || !/^[1-9][0-9]{0,9}$/.test(value) || Number(value) > 4294967295) {
    throw new Error("Reload this document before saving; its current version is unavailable.");
  }
  return value;
}

export function documentOrigin(mode: unknown): string {
  return mode === "automatic" ? "Automatic date assessment"
    : mode === "manual" ? "Workflow override" : "Origin not recorded";
}

export function documentScore(value: unknown): number | "Unknown" {
  if (value === null || value === undefined || value === "") return "Unknown";
  const numeric = Number(value);
  return Number.isFinite(numeric) && numeric >= 0 && numeric <= 100 ? numeric : "Unknown";
}

// API fields stay stored values. These named presentation fields select the
// current assessment only for server-labelled automatic records.
export function presentDocument(record: AnyRecord): AnyRecord {
  const assessment = asRecord(record.currentDateAssessment);
  const source = record.lifecycleMode === "automatic" ? assessment : record;
  return {
    ...record,
    lifecycleOrigin: documentOrigin(record.lifecycleMode),
    displayedState: source.status ?? "Unknown",
    displayedRenewal: source.renewalStatus ?? "Unknown",
    assessmentScore: documentScore(source.riskScore),
    displayedRecommendation: source.recommendedAction ?? "Assessment unavailable; reload the document.",
    assessmentDateUtc: assessment.assessmentDate ?? "Unknown",
  };
}

// Keep the export under the existing CSV helper's 24-column limit. Stored and
// current values are explicit; object references/tokens are not exported.
export function documentExport(record: AnyRecord): AnyRecord {
  const assessment = asRecord(record.currentDateAssessment);
  const view = presentDocument(record);
  return {
    documentNumber: record.documentNumber, title: record.title,
    entityType: record.entityType, entityId: record.entityId, entityName: record.entityName,
    issuedAt: record.issuedAt, expiresAt: record.expiresAt,
    lifecycleOrigin: view.lifecycleOrigin,
    storedStatus: record.status, storedRenewal: record.renewalStatus,
    storedScore: documentScore(record.riskScore), storedRecommendation: record.recommendedAction,
    storedAssessedOnUtc: record.lifecycleAssessedOn,
    currentDateStatus: assessment.status ?? "Unknown",
    currentDateRenewal: assessment.renewalStatus ?? "Unknown",
    currentDateScore: documentScore(assessment.riskScore),
    currentDateRecommendation: assessment.recommendedAction ?? "Unknown",
    assessmentDateUtc: view.assessmentDateUtc, policyVersion: assessment.policyVersion,
    displayedState: view.displayedState, displayedScore: view.assessmentScore,
    displayedRenewal: view.displayedRenewal, displayedRecommendation: view.displayedRecommendation,
  };
}

export function documentPayload(form: AnyRecord, intent: DocumentIntent, reason: string, replaceQueuedRenewal: boolean): AnyRecord {
  const payload: AnyRecord = {};
  for (const field of DOCUMENT_METADATA_FIELDS) {
    if (Object.hasOwn(form, field)) payload[field] = form[field];
  }
  if (!form.id) {
    payload.file = form.file;
    return payload; // New upload is always server-automatic; no echoed tuple.
  }
  payload.expectedVersion = documentVersion(form.rowVersion);
  payload.lifecycleIntent = intent;
  if (intent === "preserve") return payload;
  const trimmedReason = reason.trim();
  if (!trimmedReason || trimmedReason.length > 500) throw new Error("Enter a reason of 1–500 characters for the lifecycle change.");
  payload.lifecycleReason = trimmedReason;
  payload.replaceQueuedRenewal = replaceQueuedRenewal;
  if (intent === "automatic") return payload;
  if (!DOCUMENT_STATUSES.includes(form.status as typeof DOCUMENT_STATUSES[number])
    || !DOCUMENT_RENEWAL_STATUSES.includes(form.renewalStatus as typeof DOCUMENT_RENEWAL_STATUSES[number])) {
    throw new Error("Choose a supported status and renewal status for the workflow override.");
  }
  let risk: number | null = null;
  if (form.riskScore !== null) {
    const raw = String(form.riskScore ?? "").trim();
    if (raw.length > 32 || !/^[0-9]+(?:\.[0-9]+)?$/.test(raw) || !Number.isFinite(Number(raw)) || Number(raw) > 100) {
      throw new Error("Enter a score from 0 to 100, or explicitly select Unknown score.");
    }
    risk = Number(raw);
  }
  const recommendation = String(form.recommendedAction ?? "").trim();
  if (!recommendation || recommendation.length > 240) throw new Error("Enter a recommended action of 1–240 characters.");
  return { ...payload, status: form.status, renewalStatus: form.renewalStatus, riskScore: risk, recommendedAction: recommendation };
}

// Preview only: the server captures its own UTC day when it accepts the write.
export function previewDocumentDate(expiry: unknown, today = new Date().toISOString().slice(0, 10)): AnyRecord {
  const raw = String(expiry ?? "").trim();
  if (!raw) return { status: "Unknown", riskScore: null, renewalStatus: "Unknown", assessmentDate: today };
  const date = raw.slice(0, 10);
  const at = Date.parse(`${date}T00:00:00Z`), start = Date.parse(`${today}T00:00:00Z`);
  if (!/^\d{4}-\d{2}-\d{2}$/.test(date) || !Number.isFinite(at) || new Date(at).toISOString().slice(0, 10) !== date || !Number.isFinite(start)) {
    return { status: "Invalid date", riskScore: null, renewalStatus: "Unknown", assessmentDate: today };
  }
  return at < start ? { status: "Expired", riskScore: 90, renewalStatus: "Renewal Required", assessmentDate: today }
    : at <= start + 30 * 86400000 ? { status: "Expiring", riskScore: 60, renewalStatus: "Renewal Required", assessmentDate: today }
    : { status: "Active", riskScore: 25, renewalStatus: "Current", assessmentDate: today };
}
