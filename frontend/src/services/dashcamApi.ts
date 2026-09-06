import { apiClient, unwrap } from "@/services/apiClient";
import { sessionBoundRequest, SessionChangedBeforeRequestError } from "@/auth/requestSessionGuard";
import type { AnyRecord, UserSession } from "@/types";

export const CAMERA_NOTICE = "Stored metadata only. Media, provider and automated assessments are not provided or verified by this view.";
export const CAMERA_FIELDS = ["eventType", "title", "severity", "safetyEventId", "driverId", "vehicleId", "jobId", "routeId", "locationDescription", "occurredAt"] as const;
const references = new Set(["safetyEventId", "driverId", "vehicleId", "jobId", "routeId"]);
const severities = new Set(["Low", "Medium", "High", "Critical"]);
const own = (value: object, key: string) => Object.prototype.hasOwnProperty.call(value, key);
const plain = (value: unknown): value is AnyRecord => value !== null && typeof value === "object"
  && (Object.getPrototypeOf(value) === Object.prototype || Object.getPrototypeOf(value) === null);
export class CameraMetadataError extends Error {
  constructor(public readonly kind: "validation" | "session" | "rejected" | "unconfirmed", message: string) { super(message); }
}
const invalid = () => new CameraMetadataError("validation", "Review the manual metadata fields, identifiers and UTC time. No request was sent.");
const unconfirmed = () => new CameraMetadataError("unconfirmed", "The metadata outcome is unconfirmed. Inspect the records before deliberately submitting again.");
export const cameraErrorMessage = (error: unknown) => error instanceof CameraMetadataError ? error.message : "Metadata is unavailable. Inspect the records before submitting again.";

export function cameraId(value: unknown): string | null {
  if (typeof value === "number") return Number.isSafeInteger(value) && value > 0 ? String(value) : null;
  return typeof value === "string" && /^[1-9]\d{0,18}$/.test(value) && BigInt(value) <= 9223372036854775807n ? value : null;
}
export function cameraVersion(value: unknown): number | null {
  if (typeof value === "number") return Number.isSafeInteger(value) && value >= 0 ? value : null;
  if (typeof value !== "string" || !/^(0|[1-9]\d*)$/.test(value)) return null;
  const number = Number(value);
  return Number.isSafeInteger(number) && number >= 0 ? number : null;
}
// Only these own spellings are understood; competing/malformed core values
// cannot be repaired by generic key normalization or string coercion.
function field(row: AnyRecord, key: string): { valid: boolean; present: boolean; value: unknown } {
  const snake = key.replace(/[A-Z]/g, (letter) => `_${letter.toLowerCase()}`);
  const aliases = [...new Set([key, snake])];
  const comparable = key.replaceAll("_", "").toLowerCase();
  if (Object.keys(row).some((candidate) => candidate.replaceAll("_", "").toLowerCase() === comparable && !aliases.includes(candidate))) return { valid: false, present: false, value: undefined };
  const present = aliases.filter((alias) => own(row, alias));
  if (present.length > 1 && row[present[0]] !== row[present[1]]) return { valid: false, present: true, value: undefined };
  return { valid: true, present: present.length > 0, value: present.length ? row[present[0]] : undefined };
}
export function cameraRecord(raw: unknown) {
  if (!plain(raw)) return null;
  const id = field(raw, "id"), version = field(raw, "rowVersion"), authority = field(raw, "sourceAuthority"), deletion = field(raw, "deletedAt");
  if (![id, version, authority, deletion].every((item) => item.valid) || !cameraId(id.value)) return null;
  const values: AnyRecord = { id: cameraId(id.value) };
  for (const key of ["eventNumber", ...CAMERA_FIELDS, "driverName", "vehicleCode", "jobNumber", "routeCode"]) {
    const item = field(raw, key);
    if (!item.valid) return null;
    if (!item.present) continue;
    if (references.has(key)) {
      if (item.value !== null && !cameraId(item.value)) return null;
      values[key] = item.value === null ? null : cameraId(item.value);
    }
    else {
      if (item.value !== null && typeof item.value !== "string") return null;
      values[key] = key === "severity" && typeof item.value === "string" && !severities.has(item.value) ? "Unavailable" : item.value;
    }
  }
  const source = authority.value === "LegacyUnverified" ? "Manual metadata — unverified"
    : authority.value === "ProviderPending" ? "Provider pending — not verified in this view"
      : authority.value === "Authoritative" ? "Stored provider authority — not verified in this view" : "Source authority unavailable";
  return { id: cameraId(id.value)!, version: cameraVersion(version.value), values,
    manual: authority.present && authority.value === "LegacyUnverified" && deletion.present && deletion.value === null && cameraVersion(version.value) !== null,
    source, authority: authority.value, deletion: deletion.value };
}
export function cameraProjection(raw: unknown): AnyRecord | null {
  const record = cameraRecord(raw);
  if (!record) return null;
  const { safetyEventId: _link, ...values } = record.values;
  return { ...values, metadataNotice: CAMERA_NOTICE };
}
export function cameraUtc(value: unknown, now = Date.now()): string | null {
  if (typeof value !== "string") return null;
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.(\d{1,7}))?Z$/.exec(value);
  if (!match || /[1-9]/.test((match[7] ?? "").slice(3))) return null;
  const [year, month, day, hour, minute, second] = match.slice(1, 7).map(Number);
  if (year < 1 || month < 1 || month > 12 || day < 1 || hour > 23 || minute > 59 || second > 59) return null;
  const date = new Date(0);
  date.setUTCFullYear(year, month - 1, day);
  date.setUTCHours(hour, minute, second, Number((match[7] ?? "").padEnd(3, "0").slice(0, 3)));
  if (date.getUTCFullYear() !== year || date.getUTCMonth() !== month - 1 || date.getUTCDate() !== day || date.getTime() > now + 300000) return null;
  return date.toISOString();
}
export function cameraPayload(raw: unknown, update: boolean): AnyRecord {
  if (!plain(raw)) throw invalid();
  const allowed = new Set<string>(CAMERA_FIELDS.filter((key) => !update || key !== "safetyEventId"));
  if (update) allowed.add("rowVersion");
  if (Reflect.ownKeys(raw).some((key) => typeof key !== "string" || !allowed.has(key))) throw invalid();
  const result: AnyRecord = {};
  if (update) {
    if (!own(raw, "rowVersion") || typeof raw.rowVersion !== "number" || cameraVersion(raw.rowVersion) === null) throw invalid();
    result.rowVersion = raw.rowVersion;
  }
  for (const key of CAMERA_FIELDS) {
    if (!own(raw, key)) {
      if (!update && ["eventType", "title", "severity"].includes(key)) throw invalid();
      continue;
    }
    const value = raw[key];
    if (references.has(key)) {
      if (value !== null && (typeof value !== "number" || !Number.isSafeInteger(value) || value <= 0)) throw invalid();
      result[key] = value;
    } else if (key === "occurredAt") {
      if (value !== null && cameraUtc(value) === null) throw invalid();
      result[key] = value === null ? null : cameraUtc(value);
    } else if (key === "locationDescription" && value === null) result[key] = null;
    else {
      if (typeof value !== "string") throw invalid();
      const text = value.trim();
      const limit = key === "eventType" ? 120 : 220;
      if (text.length > limit || !text || (key === "severity" && !severities.has(value))) throw invalid();
      result[key] = text;
    }
  }
  if (update && Object.keys(result).length === 1) throw new CameraMetadataError("validation", "No metadata changes to submit.");
  return Object.freeze(result);
}
export type CameraEditor = { generation: number; session: UserSession; id: string | null; version: number | null; initial: AnyRecord; draft: Record<string, string>; revision: number; selection: string | null; record?: unknown };
export function cameraDraft(values: AnyRecord = {}): Record<string, string> {
  return Object.fromEntries(CAMERA_FIELDS.map((key) => [key, values[key] == null ? "" : typeof values[key] === "string" || typeof values[key] === "number" ? String(values[key]) : ""]));
}
export function cameraDraftPayload(editor: CameraEditor): AnyRecord {
  const payload: AnyRecord = editor.id === null ? {} : { rowVersion: editor.version };
  for (const key of CAMERA_FIELDS) {
    if (editor.id !== null && key === "safetyEventId") continue;
    const text = editor.draft[key];
    if (typeof text !== "string") throw invalid();
    if (key === "severity" && !severities.has(text)) throw invalid();
    const original = cameraDraft(editor.initial)[key];
    if (editor.id !== null && text === original) continue;
    if (editor.id === null && text === "" && !["eventType", "title", "severity"].includes(key)) continue;
    if (references.has(key)) {
      if (text === "") payload[key] = null;
      else {
        if (!cameraId(text) || !Number.isSafeInteger(Number(text))) throw invalid();
        payload[key] = Number(text);
      }
    } else payload[key] = (key === "locationDescription" && text.trim() === "") || (key === "occurredAt" && text === "") ? null : text;
    if (editor.id !== null) {
      const previous = editor.initial[key];
      const next = key === "occurredAt" ? (payload[key] === null ? null : cameraUtc(payload[key]))
        : typeof payload[key] === "string" ? payload[key].trim() : payload[key];
      if (key === "occurredAt" && payload[key] !== null && next === null) throw invalid();
      const normalizedPrevious = key === "occurredAt" ? (previous === null ? null : cameraUtc(previous))
        : references.has(key) ? (previous === null ? null : cameraId(previous))
          : typeof previous === "string" ? previous.trim() : previous;
      const equal = references.has(key) ? (next === null && normalizedPrevious === null) || (typeof next === "number" && String(next) === normalizedPrevious)
        : next === normalizedPrevious && (key !== "occurredAt" || previous === null || normalizedPrevious !== null);
      if (equal) delete payload[key];
    }
  }
  return cameraPayload(payload, editor.id !== null);
}
export function cameraSession(session: UserSession | null | undefined): UserSession | null {
  if (!session || typeof session.token !== "string" || !session.token || !cameraId(session.user?.id) || !cameraId(session.company?.id) || session.supportAccess?.mode === "read_only") return null;
  return { ...session, user: { ...session.user }, company: { ...session.company }, permissions: [...session.permissions] };
}
export function sameCameraSession(left: UserSession | null | undefined, right: UserSession | null | undefined): boolean {
  return Boolean(left && right && left.token === right.token && cameraId(left.user?.id) === cameraId(right.user?.id) && cameraId(left.company?.id) === cameraId(right.company?.id));
}
export type CameraReceipt = { id: string; rowVersion: number; dataSource: "stored_metadata"; provenanceStatus: "unverified"; mediaAvailable: false; automatedAssessmentAvailable: false };
async function writeCamera(id: string | null, input: unknown, session: UserSession | undefined): Promise<CameraReceipt> {
  const payload = cameraPayload(input, id !== null);
  const captured = cameraSession(session);
  if (!captured) throw new CameraMetadataError("session", "Your session changed. Reopen metadata in the current session.");
  try {
    const config = sessionBoundRequest(captured);
    const response = id === null ? await apiClient.post("/api/dashcam/events", payload, config)
      : await apiClient.put(`/api/dashcam/events/${id}`, payload, config);
    const envelope = response.data;
    if (response.status !== (id === null ? 201 : 200) || !plain(envelope) || !field(envelope, "success").valid || !field(envelope, "data").valid
      || !own(envelope, "success") || envelope.success !== true || !own(envelope, "data") || !plain(envelope.data)) throw unconfirmed();
    const data = envelope.data;
    const keys = ["id", "rowVersion", "dataSource", "provenanceStatus", "mediaAvailable", "automatedAssessmentAvailable"];
    if (Reflect.ownKeys(data).length !== keys.length || keys.some((key) => !own(data, key)) || !cameraId(data.id)
      || typeof data.rowVersion !== "number" || cameraVersion(data.rowVersion) === null || (id !== null && cameraId(data.id) !== id)
      || data.dataSource !== "stored_metadata" || data.provenanceStatus !== "unverified" || data.mediaAvailable !== false || data.automatedAssessmentAvailable !== false) throw unconfirmed();
    return Object.freeze({ id: cameraId(data.id)!, rowVersion: data.rowVersion, dataSource: "stored_metadata", provenanceStatus: "unverified", mediaAvailable: false, automatedAssessmentAvailable: false });
  } catch (error) {
    if (error instanceof CameraMetadataError) throw error;
    if (error instanceof SessionChangedBeforeRequestError) throw new CameraMetadataError("session", "Your session changed before the metadata request. Reopen it in the current session.");
    const status = (error as { response?: { status?: number } })?.response?.status;
    const messages: Record<number, string> = { 400: "The metadata fields were rejected. Review the values.", 403: "Manual metadata access was denied.", 404: "This manual metadata record is unavailable.", 409: "The record changed. Refresh it before editing again.", 503: "Manual metadata is temporarily unavailable." };
    if (status && messages[status]) throw new CameraMetadataError("rejected", messages[status]);
    throw unconfirmed();
  }
}

export const dashcamApi = {
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/dashcam/summary")),
  events: async () => { const rows = await unwrap<AnyRecord[]>(apiClient.get("/api/dashcam/events")); if (!Array.isArray(rows)) throw new CameraMetadataError("rejected", "Stored camera records are unavailable."); return rows; },
  detail: (id: string | number) => { const key = cameraId(id); if (!key) throw invalid(); return unwrap<AnyRecord>(apiClient.get(`/api/dashcam/events/${key}`)); },
  create: (payload: AnyRecord, session?: UserSession) => writeCamera(null, payload, session),
  update: (id: string | number, payload: AnyRecord, session?: UserSession) => { const key = cameraId(id); if (!key) throw invalid(); return writeCamera(key, payload, session); },
};
