import { useRef, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { AlertTriangle, CheckCircle2, Download, FileDown, FileUp, Loader2, Upload, X } from "lucide-react";
import { downloadServerExport } from "@/services/fleetDomainApi";
import type { AnyRecord } from "@/types";

/* ============================================================
   ENTITY IMPORT / EXPORT / TEMPLATE
   Shared toolbar + import wizard for master-data entities
   (vehicles, drivers). The full pipeline is real:
     Template  → server CSV with canonical headers
     Import    → client CSV parse → server validation preview
                 (create/update/error per row) → committed upsert
     Export    → server CSV of the FULL tenant dataset
   No fabricated counts anywhere — every number in the wizard
   comes from the server preview/commit response.
   ============================================================ */

export interface ImportExportConfig {
  entity: string;                       // "vehicles" | "drivers" — used in labels/filenames
  columns: readonly string[];           // canonical camelCase headers (template order)
  requiredColumns: readonly string[];   // shown as required in the wizard help
  templateEndpoint: string;             // GET  → text/csv
  exportEndpoint: string;               // GET  → text/csv (full tenant dataset)
  importPreview: (rows: AnyRecord[]) => Promise<AnyRecord>;
  importCommit: (rows: AnyRecord[]) => Promise<AnyRecord>;
  invalidateKey: string;                // react-query key root to refresh after commit
}

/* ---------------- CSV parsing (quoted fields, CRLF, BOM) ---------------- */

export function parseCsv(text: string): { headers: string[]; rows: Record<string, string>[] } {
  const src = text.replace(/^﻿/, "");
  const table: string[][] = [];
  let row: string[] = [];
  let cell = "";
  let inQuotes = false;
  for (let i = 0; i < src.length; i++) {
    const ch = src[i];
    if (inQuotes) {
      if (ch === '"') {
        if (src[i + 1] === '"') { cell += '"'; i++; }
        else inQuotes = false;
      } else cell += ch;
    } else if (ch === '"') {
      inQuotes = true;
    } else if (ch === ",") {
      row.push(cell); cell = "";
    } else if (ch === "\n" || ch === "\r") {
      if (ch === "\r" && src[i + 1] === "\n") i++;
      row.push(cell); cell = "";
      if (row.some((c) => c.trim() !== "")) table.push(row);
      row = [];
    } else cell += ch;
  }
  row.push(cell);
  if (row.some((c) => c.trim() !== "")) table.push(row);
  if (table.length === 0) return { headers: [], rows: [] };

  const headers = table[0].map((h) => h.trim());
  const rows = table.slice(1).map((cells) => {
    const record: Record<string, string> = {};
    headers.forEach((h, i) => { if (h) record[h] = (cells[i] ?? "").trim(); });
    return record;
  });
  return { headers, rows };
}

// Header tolerance: match "Vehicle Code", "vehicle_code" or "vehicleCode" to the
// canonical camelCase column so hand-edited spreadsheets still import cleanly.
function canonKey(header: string, columns: readonly string[]): string | null {
  const norm = header.replace(/[\s_-]+/g, "").toLowerCase();
  return columns.find((c) => c.toLowerCase() === norm) ?? null;
}

export function normalizeRows(parsed: { headers: string[]; rows: Record<string, string>[] }, columns: readonly string[]) {
  const mapping = new Map<string, string>();
  const unknown: string[] = [];
  for (const h of parsed.headers) {
    const key = canonKey(h, columns);
    if (key) mapping.set(h, key);
    else if (h) unknown.push(h);
  }
  const rows = parsed.rows.map((r) => {
    const out: AnyRecord = {};
    for (const [raw, key] of mapping) {
      const v = r[raw];
      if (v !== undefined && v !== "") out[key] = v;
    }
    return out;
  }).filter((r) => Object.keys(r).length > 0);
  return { rows, matchedColumns: [...new Set(mapping.values())], unknownColumns: unknown };
}

/* ---------------- toolbar ---------------- */

export function EntityImportExport({ config, canImport, canExport }: {
  config: ImportExportConfig;
  canImport: boolean;
  canExport: boolean;
}) {
  const [wizardOpen, setWizardOpen] = useState(false);
  const [busy, setBusy] = useState<"template" | "export" | null>(null);
  const [toolbarError, setToolbarError] = useState<string | null>(null);

  const download = async (kind: "template" | "export", endpoint: string, filename: string) => {
    setBusy(kind);
    setToolbarError(null);
    try {
      await downloadServerExport(endpoint, filename);
    } catch {
      setToolbarError(kind === "template" ? "Template download failed — check your connection." : "Export failed — the server did not return the file.");
    } finally {
      setBusy(null);
    }
  };

  return (
    <div className="flex flex-wrap items-center gap-2">
      <button type="button" className="btn-ghost h-10" disabled={busy === "template"}
        onClick={() => download("template", config.templateEndpoint, `${config.entity}-import-template.csv`)}>
        {busy === "template" ? <Loader2 className="h-4 w-4 animate-spin" /> : <FileDown className="h-4 w-4" />} Template
      </button>
      <button type="button" className="btn-ghost h-10" disabled={!canImport} title={canImport ? "Import a CSV of records" : "Requires fleet manage permission"}
        onClick={() => canImport && setWizardOpen(true)}>
        <FileUp className="h-4 w-4" /> Import
      </button>
      <button type="button" className="btn-ghost h-10" disabled={!canExport || busy === "export"} title={canExport ? "Export the full dataset (all pages)" : "Requires export permission"}
        onClick={() => canExport && download("export", config.exportEndpoint, `${config.entity}_${new Date().toISOString().slice(0, 10)}.csv`)}>
        {busy === "export" ? <Loader2 className="h-4 w-4 animate-spin" /> : <Download className="h-4 w-4" />} Export
      </button>
      {toolbarError && <span className="text-xs font-semibold text-rose-600">{toolbarError}</span>}
      {wizardOpen && <ImportWizard config={config} onClose={() => setWizardOpen(false)} />}
    </div>
  );
}

/* ---------------- wizard ---------------- */

type PreviewRow = { rowNumber: number; key: string; action: "create" | "update" | "error"; errors: string[] };

function ImportWizard({ config, onClose }: { config: ImportExportConfig; onClose: () => void }) {
  const queryClient = useQueryClient();
  const fileRef = useRef<HTMLInputElement>(null);
  const [step, setStep] = useState<"select" | "preview" | "done">("select");
  const [rows, setRows] = useState<AnyRecord[]>([]);
  const [unknownColumns, setUnknownColumns] = useState<string[]>([]);
  const [preview, setPreview] = useState<AnyRecord | null>(null);
  const [result, setResult] = useState<AnyRecord | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [working, setWorking] = useState(false);

  const ingestText = async (text: string) => {
    setError(null);
    const parsed = parseCsv(text);
    if (!parsed.rows.length) { setError("No data rows found in that file."); return; }
    const { rows: normalized, matchedColumns, unknownColumns: unknown } = normalizeRows(parsed, config.columns);
    if (!matchedColumns.length) {
      setError(`No recognizable columns. Expected headers like: ${config.columns.slice(0, 4).join(", ")}… — download the template for the exact format.`);
      return;
    }
    if (normalized.length > 500) { setError("Imports are limited to 500 rows per file. Split the file and retry."); return; }
    setUnknownColumns(unknown);
    setRows(normalized);
    setWorking(true);
    try {
      const p = await config.importPreview(normalized);
      setPreview(p);
      setStep("preview");
    } catch (e) {
      setError(e instanceof Error ? e.message : "Preview failed — the server rejected the rows.");
    } finally {
      setWorking(false);
    }
  };

  const onFile = async (file: File | undefined) => {
    if (!file) return;
    await ingestText(await file.text());
  };

  const commit = async () => {
    setWorking(true);
    setError(null);
    try {
      const r = await config.importCommit(rows);
      setResult(r);
      setStep("done");
      await queryClient.invalidateQueries({ queryKey: [config.invalidateKey] });
    } catch (e) {
      setError(e instanceof Error ? e.message : "Import failed — no rows were guaranteed committed.");
    } finally {
      setWorking(false);
    }
  };

  const previewRows = ((preview?.rows as PreviewRow[]) || []);
  const importable = Number(preview?.creates ?? 0) + Number(preview?.updates ?? 0);

  return (
    <div className="fixed inset-0 z-[70] grid place-items-center bg-slate-900/40 p-4 backdrop-blur-sm" onClick={onClose}>
      <div className="fc-neumo flex max-h-[86vh] w-full max-w-2xl flex-col overflow-hidden anim-fade-up" onClick={(e) => e.stopPropagation()}>
        <div className="flex shrink-0 items-start justify-between px-6 pt-5">
          <div>
            <div className="text-[11px] font-bold uppercase tracking-[0.16em] text-slate-400">CSV import</div>
            <h2 className="mt-1 text-xl font-black capitalize tracking-tight text-slate-900">Import {config.entity}</h2>
          </div>
          <button type="button" aria-label="Close" onClick={onClose} className="rounded-lg p-2 text-slate-400 transition hover:bg-slate-100 hover:text-slate-700"><X className="h-5 w-5" /></button>
        </div>

        <div className="min-h-0 flex-1 overflow-y-auto px-6 py-5">
          {step === "select" && (
            <div className="space-y-4">
              <button
                type="button"
                onClick={() => fileRef.current?.click()}
                className="deck-inset grid w-full place-items-center rounded-2xl px-6 py-10 text-center transition hover:brightness-[1.02]"
              >
                <Upload className="h-8 w-8 text-slate-400" />
                <p className="mt-3 text-sm font-bold text-slate-700">Choose a CSV file</p>
                <p className="mt-1 text-xs text-slate-500">
                  Up to 500 rows. Required: {config.requiredColumns.join(", ")}. Matching {config.entity === "vehicles" ? "codes" : "codes"} update existing records; new ones are created.
                </p>
              </button>
              <input ref={fileRef} type="file" accept=".csv,text/csv" aria-label="Choose a CSV file to import" className="hidden" onChange={(e) => void onFile(e.target.files?.[0])} />
              <p className="text-center text-[11px] font-medium text-slate-400">
                Column headers accepted in any of these forms: <code className="rounded bg-slate-100 px-1">vehicleCode</code>, <code className="rounded bg-slate-100 px-1">vehicle_code</code>, <code className="rounded bg-slate-100 px-1">Vehicle Code</code>
              </p>
              {working && <p className="flex items-center justify-center gap-2 text-sm font-semibold text-slate-500"><Loader2 className="h-4 w-4 animate-spin" /> Validating rows on the server…</p>}
            </div>
          )}

          {step === "preview" && preview && (
            <div className="space-y-4">
              <div className="grid grid-cols-3 gap-2.5">
                <WizardStat label="To create" value={Number(preview.creates ?? 0)} tone="text-emerald-700" />
                <WizardStat label="To update" value={Number(preview.updates ?? 0)} tone="text-sky-700" />
                <WizardStat label="Errors" value={Number(preview.invalid ?? 0)} tone={Number(preview.invalid ?? 0) > 0 ? "text-rose-700" : "text-slate-700"} />
              </div>
              {unknownColumns.length > 0 && (
                <p className="rounded-xl border border-amber-200 bg-amber-50 px-3 py-2 text-xs font-medium text-amber-800">
                  Ignored unrecognized columns: {unknownColumns.join(", ")}
                </p>
              )}
              <div className="deck-inset max-h-64 overflow-y-auto rounded-xl">
                <table className="w-full text-left text-xs">
                  <thead className="sticky top-0 bg-[#e9f0f9]">
                    <tr className="text-[10px] uppercase tracking-wide text-slate-400">
                      <th className="px-3 py-2 font-bold">Row</th>
                      <th className="px-3 py-2 font-bold">Key</th>
                      <th className="px-3 py-2 font-bold">Action</th>
                      <th className="px-3 py-2 font-bold">Issues</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-200/60">
                    {previewRows.map((r) => (
                      <tr key={r.rowNumber}>
                        <td className="px-3 py-2 tabular-nums text-slate-500">{r.rowNumber}</td>
                        <td className="px-3 py-2 font-semibold text-slate-800">{r.key || "—"}</td>
                        <td className="px-3 py-2">
                          <span className={`rounded-md px-1.5 py-0.5 text-[10px] font-bold uppercase ${
                            r.action === "create" ? "bg-emerald-100 text-emerald-700" :
                            r.action === "update" ? "bg-sky-100 text-sky-700" : "bg-rose-100 text-rose-700"
                          }`}>{r.action}</span>
                        </td>
                        <td className="px-3 py-2 text-rose-600">{r.errors?.join("; ") || ""}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <p className="text-[11px] font-medium text-slate-500">
                Rows with errors are skipped on commit — the rest import normally.
              </p>
            </div>
          )}

          {step === "done" && result && (
            <div className="space-y-4 py-4 text-center">
              <CheckCircle2 className="mx-auto h-10 w-10 text-emerald-500" />
              <p className="text-lg font-black text-slate-900">Import complete</p>
              <div className="grid grid-cols-3 gap-2.5">
                <WizardStat label="Created" value={Number(result.created ?? 0)} tone="text-emerald-700" />
                <WizardStat label="Updated" value={Number(result.updated ?? 0)} tone="text-sky-700" />
                <WizardStat label="Skipped" value={((result.skipped as AnyRecord[]) || []).length} tone="text-slate-700" />
              </div>
              {((result.skipped as AnyRecord[]) || []).length > 0 && (
                <div className="deck-inset max-h-32 overflow-y-auto rounded-xl p-3 text-left">
                  {((result.skipped as AnyRecord[]) || []).map((s, i) => (
                    <p key={i} className="text-xs text-rose-600">Row {String(s.rowNumber)} ({String(s.key || "—")}): {(s.errors as string[])?.join("; ")}</p>
                  ))}
                </div>
              )}
            </div>
          )}

          {error && (
            <p className="mt-4 flex items-start gap-2 rounded-xl border border-rose-200 bg-rose-50 px-3 py-2.5 text-sm text-rose-700">
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" /> {error}
            </p>
          )}
        </div>

        <div className="flex shrink-0 justify-end gap-3 border-t border-slate-200/70 px-6 py-4">
          {step === "preview" ? (
            <>
              <button type="button" className="btn-ghost h-10" onClick={() => { setStep("select"); setPreview(null); }}>Back</button>
              <button type="button" className="btn-primary h-10" disabled={working || importable === 0} onClick={() => void commit()}>
                {working ? <Loader2 className="h-4 w-4 animate-spin" /> : <FileUp className="h-4 w-4" />}
                {working ? "Importing…" : `Import ${importable} row${importable === 1 ? "" : "s"}`}
              </button>
            </>
          ) : (
            <button type="button" className={step === "done" ? "btn-primary h-10" : "btn-ghost h-10"} onClick={onClose}>
              {step === "done" ? "Done" : "Cancel"}
            </button>
          )}
        </div>
      </div>
    </div>
  );
}

function WizardStat({ label, value, tone }: { label: string; value: number; tone: string }) {
  return (
    <div className="deck-inset rounded-xl px-3 py-2.5 text-center">
      <div className={`text-[22px] font-black leading-none tabular-nums ${tone}`}>{value}</div>
      <div className="mt-1 text-[10px] font-bold uppercase tracking-wider text-slate-400">{label}</div>
    </div>
  );
}
