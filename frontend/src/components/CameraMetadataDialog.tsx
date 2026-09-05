import { useRef, useState } from "react";
import { useMutation, type QueryClient } from "@tanstack/react-query";
import { useDialogFocus } from "@/hooks/useDialogFocus";
import { useSingleFlight } from "@/hooks/useSingleFlight";
import { exportCsv } from "@/components/ui";
import { CAMERA_FIELDS, CAMERA_NOTICE, CameraMetadataError, cameraDraft, cameraDraftPayload, cameraErrorMessage, cameraId, cameraProjection, cameraRecord, cameraSession, dashcamApi, sameCameraSession, type CameraEditor, type CameraReceipt } from "@/services/dashcamApi";
import type { AnyRecord, UserSession } from "@/types";

type CameraQuery = { data?: AnyRecord; isError: boolean; isLoading: boolean; isFetching: boolean; fetchStatus: string };
type CameraView = { enabled: boolean; session: UserSession | null; canManage: boolean; canExport: boolean; selectedId: unknown; visibleIds: string[]; detail: CameraQuery;
  rows: { data?: AnyRecord[]; isError: boolean; isLoading: boolean; isFetching: boolean; fetchStatus: string }; queryClient: QueryClient };
type Notice = { receipt: CameraReceipt; session: UserSession; generation: number; sessionGeneration: number };
type Attempt = { editor: CameraEditor; payload: AnyRecord; sessionGeneration: number };

export function useCameraMetadataWorkflow(view: CameraView) {
  const latest = useRef(view);
  latest.current = view;
  const state = useRef({ generation: 0, sessionGeneration: 0, editor: null as CameraEditor | null, attempt: null as Attempt | null,
    notice: null as Notice | null, warning: false, refreshing: false, refreshGeneration: 0, error: "" });
  const context = useRef({ session: view.session, enabled: view.enabled, canManage: view.canManage });
  if (context.current.session !== view.session || !sameCameraSession(context.current.session, view.session) || (context.current.enabled && !view.enabled)) {
    ++state.current.sessionGeneration;
    state.current.editor = null; state.current.notice = null; state.current.error = "";
    ++state.current.refreshGeneration; state.current.refreshing = false;
  } else if (context.current.canManage && !view.canManage) state.current.editor = null;
  context.current = { session: view.session, enabled: view.enabled, canManage: view.canManage };
  const [, render] = useState(0);
  const publish = () => render((value) => value + 1);
  const singleFlight = useSingleFlight();
  const live = () => latest.current.enabled && latest.current.canManage && cameraSession(latest.current.session) !== null;
  const detailReady = () => {
    const current = latest.current;
    return !current.detail.isError && !current.detail.isLoading && !current.detail.isFetching && current.detail.fetchStatus === "idle"
      && cameraRecord(current.detail.data?.record)?.id === cameraId(current.selectedId);
  };
  const admitted = (editor: CameraEditor) => {
    const current = latest.current;
    if (state.current.editor !== editor || !live() || !sameCameraSession(editor.session, current.session) || editor.selection !== cameraId(current.selectedId)) return false;
    if (editor.id === null) return true;
    const record = cameraRecord(current.detail.data?.record);
    return detailReady() && Boolean(record?.manual) && record?.id === editor.id && record.version === editor.version && current.detail.data?.record === editor.record;
  };
  const ownsNotice = (notice: Notice, generation: number) => state.current.notice === notice && state.current.refreshGeneration === generation
    && notice.sessionGeneration === state.current.sessionGeneration && latest.current.enabled && sameCameraSession(notice.session, latest.current.session);
  const refresh = async (notice: Notice) => {
    if (state.current.notice !== notice || notice.sessionGeneration !== state.current.sessionGeneration || state.current.refreshing || !latest.current.enabled || !sameCameraSession(notice.session, latest.current.session)) return;
    const generation = ++state.current.refreshGeneration;
    state.current.refreshing = true;
    state.current.warning = true;
    publish();
    const client = latest.current.queryClient;
    const matches = (query: { queryKey: readonly unknown[] }) => {
      const key = query.queryKey;
      return key[0] === "dashcam" && (key.length === 1 || (key.length === 2 && key[1] === "summary") || (key.length === 3 && key[1] === "detail" && cameraId(key[2]) === notice.receipt.id));
    };
    const queries = () => client.getQueryCache().findAll({ predicate: matches });
    const paused = () => queries().some((query) => query.state.fetchStatus === "paused");
    let stop: () => void = () => {};
    try {
      let signalPause: (value: boolean) => void = () => {};
      const pause = new Promise<boolean>((resolve) => { signalPause = resolve; });
      stop = client.getQueryCache().subscribe(() => { if (paused()) signalPause(false); });
      if (paused()) signalPause(false);
      const read = client.refetchQueries({ predicate: matches, type: "active" }, { throwOnError: true }).then(() => {
        const current = queries();
        return current.length === 3 && current.every((query) => query.isActive() && query.state.status === "success" && query.state.fetchStatus === "idle");
      }, () => false);
      const updated = await Promise.race([read, pause]);
      if (ownsNotice(notice, generation)) state.current.warning = !updated;
    } catch {
      if (ownsNotice(notice, generation)) state.current.warning = true;
    } finally {
      stop();
      if (ownsNotice(notice, generation)) { state.current.refreshing = false; publish(); }
    }
  };
  const mutation = useMutation({
    retry: false,
    mutationFn: (attempt: Attempt) => {
      if (state.current.attempt !== attempt || attempt.sessionGeneration !== state.current.sessionGeneration || !admitted(attempt.editor)) throw new CameraMetadataError("session", "The metadata context changed. Reopen the record in the current session.");
      return attempt.editor.id === null ? dashcamApi.create(attempt.payload, attempt.editor.session)
        : dashcamApi.update(attempt.editor.id, attempt.payload, attempt.editor.session);
    },
    onSuccess: (receipt, attempt) => {
      if (state.current.attempt !== attempt || attempt.sessionGeneration !== state.current.sessionGeneration || !latest.current.enabled || !sameCameraSession(attempt.editor.session, latest.current.session)) return;
      state.current.editor = null; // Revoke retained callbacks before pending releases.
      const notice = { receipt, session: attempt.editor.session, generation: attempt.editor.generation, sessionGeneration: attempt.sessionGeneration };
      ++state.current.refreshGeneration;
      state.current.refreshing = false;
      state.current.notice = notice;
      state.current.warning = true;
      state.current.error = "";
      publish();
      void refresh(notice);
    },
    onError: (error, attempt) => {
      if (state.current.attempt === attempt && attempt.sessionGeneration === state.current.sessionGeneration && state.current.editor === attempt.editor && latest.current.enabled && sameCameraSession(attempt.editor.session, latest.current.session)) {
        state.current.error = cameraErrorMessage(error); publish();
      }
    },
  });
  const open = (raw?: unknown) => {
    if (!live() || state.current.attempt || state.current.editor) return;
    const record = raw === undefined ? null : cameraRecord(raw);
    if (raw !== undefined && (!detailReady() || raw !== latest.current.detail.data?.record || !record?.manual)) return;
    const session = cameraSession(latest.current.session)!;
    const initial = record ? { ...record.values } : { eventType: "", title: "", severity: "" };
    state.current.editor = { generation: ++state.current.generation, session, id: record?.id ?? null, version: record?.version ?? null,
      initial, draft: cameraDraft(initial), revision: 0, selection: cameraId(latest.current.selectedId), record: raw };
    state.current.error = "";
    mutation.reset();
    publish();
  };
  const change = (editor: CameraEditor, key: string, value: string) => {
    if (state.current.attempt || !admitted(editor) || !CAMERA_FIELDS.includes(key as typeof CAMERA_FIELDS[number]) || (editor.id !== null && key === "safetyEventId")) return;
    state.current.editor = { ...editor, draft: { ...editor.draft, [key]: value }, revision: editor.revision + 1 };
    state.current.error = ""; publish();
  };
  const close = (editor: CameraEditor) => {
    if (state.current.attempt || state.current.editor !== editor || !sameCameraSession(editor.session, latest.current.session)) return;
    state.current.editor = null; state.current.error = ""; mutation.reset(); publish();
  };
  const submit = async (editor: CameraEditor) => {
    if (state.current.attempt || !admitted(editor)) return;
    let payload: AnyRecord;
    try { payload = cameraDraftPayload(editor); } catch (error) { state.current.error = cameraErrorMessage(error); publish(); return; }
    const attempt = { editor, payload, sessionGeneration: state.current.sessionGeneration };
    state.current.attempt = attempt; state.current.error = ""; publish();
    try { await singleFlight(() => mutation.mutateAsync(attempt)); }
    finally { if (state.current.attempt === attempt) { state.current.attempt = null; publish(); } }
  };
  const exportCurrent = (scope: "list" | "detail") => {
    const current = latest.current;
    if (!current.enabled || !current.canExport || !current.session || state.current.attempt) return;
    if (scope === "detail" && !detailReady()) return;
    if (scope === "list" && (current.rows.isError || current.rows.isLoading || current.rows.isFetching || current.rows.fetchStatus !== "idle")) return;
    const raw = scope === "detail" ? [current.detail.data?.record] : current.rows.data;
    if (!Array.isArray(raw)) return;
    const rows = raw.map(cameraProjection);
    if (rows.some((row) => row === null)) return;
    exportCsv("camera-stored-metadata", (rows as AnyRecord[]).filter((row) => scope !== "list" || current.visibleIds.includes(String(row.id))));
  };
  const visible = view.enabled && sameCameraSession(state.current.editor?.session, view.session) ? state.current.editor : null;
  const notice = view.enabled && sameCameraSession(state.current.notice?.session, view.session) ? state.current.notice : null;
  return { editor: visible, pending: state.current.attempt !== null, error: state.current.error,
    notice, warning: state.current.warning, refreshing: state.current.refreshing, detailReady: detailReady(),
    canEdit: live() && detailReady() && Boolean(cameraRecord(view.detail.data?.record)?.manual),
    open, change, close, submit, refresh, exportCurrent, canLeave: () => !state.current.attempt && !state.current.editor };
}

const labels: Record<string, string> = { eventType: "Event type", title: "Title", severity: "Severity",
  safetyEventId: "Safety event ID", driverId: "Driver ID", vehicleId: "Vehicle ID", jobId: "Job ID",
  routeId: "Route ID", locationDescription: "Location", occurredAt: "Occurrence / recorded time (UTC)" };

export function CameraMetadataDialog({ editor, pending, error, onChange, onClose, onSubmit }: {
  editor: CameraEditor; pending: boolean; error: string;
  onChange: (editor: CameraEditor, key: string, value: string) => void;
  onClose: (editor: CameraEditor) => void; onSubmit: (editor: CameraEditor) => void;
}) {
  const close = () => { if (!pending) onClose(editor); };
  const ref = useDialogFocus<HTMLDivElement>(true, close);
  let validation = "";
  try { cameraDraftPayload(editor); } catch (failure) { validation = cameraErrorMessage(failure); }
  return <div ref={ref} className="fixed inset-0 z-[60] grid place-items-center bg-black/60 p-4" role="dialog" aria-modal="true" aria-labelledby="camera-metadata-title">
    <form className="panel max-h-[90vh] w-full max-w-3xl overflow-y-auto p-6" onSubmit={(event) => { event.preventDefault(); if (!pending && !validation) onSubmit(editor); }}>
      <div className="flex items-start justify-between gap-4"><h2 id="camera-metadata-title" className="text-xl font-semibold">{editor.id ? `Edit manual metadata ${editor.id}` : "Record manual metadata"}</h2><button type="button" className="btn-ghost" disabled={pending} onClick={close}>Close</button></div>
      <p className="mt-3 text-sm text-slate-600">{CAMERA_NOTICE}</p>
      <p className="mt-2 text-sm text-slate-600">If no occurrence time is supplied on creation, the server uses its current UTC recorded time. That is not an operator-observed occurrence.</p>
      {error ? <p role="alert" className="mt-4 text-sm text-red-700">{error}</p> : null}
      <fieldset disabled={pending} className="mt-5 grid gap-4 md:grid-cols-2">
        {CAMERA_FIELDS.filter((key) => !editor.id || key !== "safetyEventId").map((key) => <label key={key}>
          <span className="field-label">{labels[key]}{["eventType", "title", "severity"].includes(key) ? " *" : ""}</span>
          {key === "severity" ? <select className="field mt-1" value={editor.draft[key]} onChange={(event) => { if (!pending) onChange(editor, key, event.target.value); }}><option value="">Select severity</option>{["Low", "Medium", "High", "Critical"].map((value) => <option key={value}>{value}</option>)}</select>
            : <input className="field mt-1" type="text" inputMode={key.endsWith("Id") ? "numeric" : undefined} required={["eventType", "title"].includes(key)}
              maxLength={key === "eventType" ? 120 : key === "title" || key === "locationDescription" ? 220 : key.endsWith("Id") ? 19 : 32}
              placeholder={key === "occurredAt" ? "YYYY-MM-DDTHH:mm:ss.sssZ (UTC)" : undefined}
              value={editor.draft[key]} onChange={(event) => { if (!pending) onChange(editor, key, event.target.value); }} />}
        </label>)}
      </fieldset>
      {validation && !error ? <p className="mt-3 text-sm text-slate-600">{validation}</p> : null}
      <div className="mt-5 flex justify-end gap-3"><button type="button" className="btn-ghost" disabled={pending} onClick={close}>Cancel</button><button type="submit" className="btn-primary" disabled={pending || Boolean(validation)}>{pending ? "Sending metadata…" : "Save manual metadata"}</button></div>
    </form>
  </div>;
}
