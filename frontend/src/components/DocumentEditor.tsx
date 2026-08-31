import { useId, useState, type FormEvent } from "react";
import { X } from "lucide-react";
import { useDialogFocus } from "@/hooks/useDialogFocus";
import { apiErrorMessage } from "@/utils/apiErrorMessage";
import { documentOrigin, documentPayload, documentScore, previewDocumentDate, DOCUMENT_METADATA_FIELDS, DOCUMENT_RENEWAL_STATUSES, DOCUMENT_STATUSES, type DocumentIntent } from "@/utils/documentLifecycle";
import type { AnyRecord } from "@/types";

type Props = {
  initial: AnyRecord;
  fields: string[][];
  saving: boolean;
  error: string | null;
  requiresReload: boolean;
  onClose: () => void;
  onSave: (payload: AnyRecord) => void;
  onReload: () => Promise<AnyRecord>;
};

export function DocumentEditor({ initial, fields, saving, error, requiresReload, onClose, onSave, onReload }: Props) {
  const [base, setBase] = useState(initial);
  const [form, setForm] = useState<AnyRecord>(initial);
  const [intent, setIntent] = useState<DocumentIntent>("preserve");
  const [reason, setReason] = useState("");
  const [replaceQueue, setReplaceQueue] = useState(false);
  const [localError, setLocalError] = useState<string | null>(null);
  const [reloading, setReloading] = useState(false);
  const [reloaded, setReloaded] = useState(false);
  const titleId = useId();
  const busy = saving || reloading;
  const close = () => { if (!busy) onClose(); };
  const dialogRef = useDialogFocus<HTMLFormElement>(true, close);
  const creating = !base.id;
  const uploadReady = !creating || (form.file instanceof File && String(form.title ?? "").trim() !== "" && String(form.entityId ?? "").trim() !== "");
  const expiry = String(form.expiresAt ?? "").trim() ? form.expiresAt : base.expiresAt;
  const preview = previewDocumentDate(expiry);
  const replacesQueue = base.renewalStatus === "Renewal Queued" && (intent === "automatic" || (intent === "manual" && form.renewalStatus !== "Renewal Queued"));
  const setField = (key: string, value: unknown) => setForm(current => ({ ...current, [key]: value }));

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (busy || requiresReload || !uploadReady) return;
    setLocalError(null);
    if (replacesQueue && !replaceQueue) { setLocalError("Acknowledge replacing the stored renewal queue marker before saving."); return; }
    try { onSave(documentPayload(form, intent, reason, replaceQueue)); }
    catch (failure) { setLocalError(failure instanceof Error ? failure.message : "Review the document fields before saving."); }
  };

  const reload = async () => {
    if (busy || creating) return;
    setReloading(true); setLocalError(null);
    try {
      const fresh = await onReload();
      setBase(fresh);
      // Keep entered metadata for comparison; refresh only authoritative state.
      setForm(current => ({ ...fresh, ...Object.fromEntries(DOCUMENT_METADATA_FIELDS.filter(key => Object.hasOwn(current, key)).map(key => [key, current[key]])) }));
      setIntent("preserve"); setReason(""); setReplaceQueue(false); setReloaded(true);
    } catch (failure) { setLocalError(apiErrorMessage(failure, "The current document could not be loaded. No retry was sent.")); }
    finally { setReloading(false); }
  };

  return (
    <div className="fixed inset-0 z-[60] grid place-items-center bg-black/60 p-4">
      <form ref={dialogRef} role="dialog" aria-modal="true" aria-labelledby={titleId} className="panel max-h-[90vh] w-full max-w-4xl overflow-y-auto p-6" onSubmit={submit}>
        <div className="flex items-center justify-between gap-4">
          <h2 id={titleId} className="text-2xl font-semibold text-slate-900">{creating ? "Upload Document" : "Edit document"}</h2>
          <button type="button" aria-label="Close document editor" className="icon-btn" disabled={busy} onClick={close}><X /></button>
        </div>
        <p className="mt-3 text-sm text-slate-600">{creating ? "New uploads use automatic expiry-date assessment." : `${documentOrigin(base.lifecycleMode)} · Recorded status: ${String(base.status ?? "Unknown")} · Recorded expiry: ${String(base.expiresAt ?? "Unknown").slice(0, 10)}`}</p>
        <p className="mt-1 text-sm text-slate-600">Date-window indicators are not proof of legal compliance or insurance coverage. Blank dates preserve the recorded dates when editing.</p>
        {reloaded ? <p role="status" className="mt-3 text-sm text-amber-800">Current record reloaded. Your entered metadata is retained; compare it with the current recorded values before saving. Any lifecycle override must be selected again.</p> : null}
        <fieldset disabled={busy || requiresReload} className="contents">
          {creating ? <label className="mt-6 block"><span className="mb-2 block text-sm font-semibold text-slate-700">Document file *</span><input className="field" type="file" required accept=".pdf,.png,.jpg,.jpeg,.gif,.webp,.heic,.heif,.docx,.xlsx,.txt,.csv" onChange={event => setField("file", event.target.files?.[0])} /><span className="mt-1 block text-xs text-slate-600">PDF, image, Word, Excel, text or CSV; maximum 25 MB.</span></label> : null}
          <div className="mt-6 grid gap-4 md:grid-cols-2">
            {fields.filter(([key]) => DOCUMENT_METADATA_FIELDS.includes(key as typeof DOCUMENT_METADATA_FIELDS[number])).map(([key, label]) => <label key={key}>
              <span className="mb-2 block text-sm font-semibold text-slate-700">{label}{creating && ["title", "entityType", "entityId"].includes(key) ? " *" : ""}</span>
              <input className="field" required={creating && ["title", "entityType", "entityId"].includes(key)} value={String(form[key] ?? "")} onChange={event => setField(key, event.target.value)} />
            </label>)}
          </div>
          {!creating ? <label className="mt-6 block"><span className="mb-2 block text-sm font-semibold text-slate-700">Lifecycle handling</span>
            <select className="field" value={intent} onChange={event => { setIntent(event.target.value as DocumentIntent); setReplaceQueue(false); }}>
              <option value="preserve">Keep current lifecycle handling</option><option value="automatic">Use automatic expiry assessment</option><option value="manual">Set an explicit workflow override</option>
            </select>
          </label> : null}
          {(creating || intent === "automatic" || (intent === "preserve" && base.lifecycleMode === "automatic")) ? <p className="mt-3 text-sm text-slate-700">Date preview at {String(preview.assessmentDate)} UTC: {String(preview.status)} · Score {String(documentScore(preview.riskScore))} · {String(preview.renewalStatus)}. The server reassesses when saved.</p> : null}
          {intent === "manual" && !creating ? <div className="mt-4 grid gap-4 md:grid-cols-2">
            <label><span className="mb-2 block text-sm font-semibold text-slate-700">Override status</span><select className="field" value={String(form.status ?? "")} required onChange={event => setField("status", event.target.value)}><option value="">Choose status</option>{DOCUMENT_STATUSES.map(value => <option key={value}>{value}</option>)}</select></label>
            <label><span className="mb-2 block text-sm font-semibold text-slate-700">Override renewal status</span><select className="field" value={String(form.renewalStatus ?? "")} required onChange={event => setField("renewalStatus", event.target.value)}><option value="">Choose renewal status</option>{DOCUMENT_RENEWAL_STATUSES.map(value => <option key={value}>{value}</option>)}</select></label>
            <div><label><span className="mb-2 block text-sm font-semibold text-slate-700">Override score (0–100)</span><input className="field" inputMode="decimal" disabled={form.riskScore === null} value={form.riskScore === null ? "" : String(form.riskScore ?? "")} onChange={event => setField("riskScore", event.target.value)} /></label><label className="mt-2 flex items-center gap-2 text-sm text-slate-700"><input type="checkbox" checked={form.riskScore === null} onChange={event => setField("riskScore", event.target.checked ? null : "")} />Unknown score</label></div>
            <label><span className="mb-2 block text-sm font-semibold text-slate-700">Override recommended action</span><textarea className="field" required maxLength={240} value={String(form.recommendedAction ?? "")} onChange={event => setField("recommendedAction", event.target.value)} /></label>
          </div> : null}
          {intent !== "preserve" && !creating ? <label className="mt-4 block"><span className="mb-2 block text-sm font-semibold text-slate-700">Reason for lifecycle change</span><textarea className="field" required maxLength={500} value={reason} onChange={event => setReason(event.target.value)} /></label> : null}
          {replacesQueue ? <label className="mt-4 flex items-start gap-3 text-sm text-amber-800"><input className="mt-1" type="checkbox" checked={replaceQueue} onChange={event => setReplaceQueue(event.target.checked)} />I acknowledge replacing the stored renewal queue marker. This does not cancel or complete a provider renewal.</label> : null}
        </fieldset>
        {error || localError ? <p role="alert" className="mt-4 rounded-xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700">{localError || error}</p> : null}
        {requiresReload ? <div className="mt-4 text-sm text-amber-800"><p>No automatic retry was sent. {creating ? "Refresh the vault and check this document number before attempting another upload." : "Reload and compare the current record before another save."}</p>{!creating ? <button type="button" className="btn-ghost mt-2" disabled={busy} onClick={() => void reload()}>Reload current document</button> : null}</div> : null}
        <div className="mt-6 flex justify-end gap-3"><button type="button" className="btn-ghost" disabled={busy} onClick={close}>Cancel</button><button type="submit" className="btn-primary" disabled={busy || requiresReload || !uploadReady}>{saving ? (creating ? "Uploading…" : "Saving…") : creating ? "Upload" : "Save"}</button></div>
      </form>
    </div>
  );
}
