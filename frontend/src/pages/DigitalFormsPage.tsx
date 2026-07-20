import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient, unwrap } from "@/services/apiClient";
import { exportCsv, LoadingState, EmptyState } from "@/components/ui";
import type { AnyRecord } from "@/types";

// ── Live data ─────────────────────────────────────────────────────────────────

const FORM_TEMPLATES: AnyRecord[] = [
  { id: 1, formKey: "pre-trip",     title: "Pre-Trip Inspection",         category: "Inspection",  fields: 22, requiredRole: "Driver",    frequency: "Daily",    compliance: "FMCSA / UAE MOI", active: true  },
  { id: 2, formKey: "post-trip",    title: "Post-Trip Inspection",        category: "Inspection",  fields: 18, requiredRole: "Driver",    frequency: "Daily",    compliance: "FMCSA / UAE MOI", active: true  },
  { id: 3, formKey: "dvir",         title: "Driver Vehicle Inspection (DVIR)", category: "DVIR",   fields: 31, requiredRole: "Driver",    frequency: "Per trip", compliance: "49 CFR 396.11",   active: true  },
  { id: 4, formKey: "incident",     title: "Incident Report",             category: "Safety",      fields: 28, requiredRole: "Driver",    frequency: "On event", compliance: "Internal policy", active: true  },
  { id: 5, formKey: "delivery-pod", title: "Proof of Delivery",           category: "Delivery",    fields: 14, requiredRole: "Driver",    frequency: "Per stop", compliance: "Customer SLA",    active: true  },
  { id: 6, formKey: "fuel-slip",    title: "Fuel Transaction Record",     category: "Fuel",        fields: 9,  requiredRole: "Driver",    frequency: "Per fill", compliance: "Internal policy", active: true  },
  { id: 7, formKey: "reefer-check", title: "Reefer Temperature Log",      category: "Cold Chain",  fields: 12, requiredRole: "Driver",    frequency: "Every 4h",compliance: "HACCP / GMP",     active: true  },
  { id: 8, formKey: "safety-obs",   title: "Safety Observation Card",     category: "Safety",      fields: 8,  requiredRole: "Any",       frequency: "On event", compliance: "HSE policy",      active: true  },
  { id: 9, formKey: "load-check",   title: "Load Securement Checklist",   category: "Compliance",  fields: 16, requiredRole: "Driver",    frequency: "Pre-dispatch", compliance: "OSHA / MOT", active: true  },
  { id: 10,formKey: "customer-onboarding", title: "Customer Onboarding Form", category: "CRM",    fields: 24, requiredRole: "Sales",     frequency: "Once",     compliance: "Internal",        active: false },
];

const formsApi = {
  templates: () => unwrap<AnyRecord[]>(apiClient.get("/api/digital-forms/templates")).then((rows) =>
      rows.map((r) => ({ ...r, formKey: r.formKey ?? r.form_key ?? "", fields: Number(r.fields ?? r.field_count ?? 0) }))
    ),
  submissions: () => unwrap<AnyRecord[]>(apiClient.get("/api/digital-forms/submissions")).then((rows) =>
      rows.map((r) => ({
        ...r,
        formTitle: r.formTitle ?? r.form_title ?? r.title ?? "",
        submittedBy: r.submittedBy ?? r.submitted_by ?? "",
        vehicleCode: r.vehicleCode ?? r.vehicle_code ?? "",
        submittedAt: r.submittedAt ?? r.submitted_at ?? "",
        defects: Number(r.defects ?? 0),
      }))
    ),
};

// ── Helpers ──────────────────────────────────────────────────────────────────

function CategoryBadge({ cat }: { cat: string }) {
  const cls: Record<string, string> = {
    Inspection: "bg-blue-50 border-blue-200 text-blue-700",
    DVIR:       "bg-teal-50 border-teal-200 text-teal-700",
    Safety:     "bg-red-50 border-red-200 text-red-700",
    Delivery:   "bg-green-50 border-green-200 text-green-700",
    Fuel:       "bg-amber-50 border-amber-200 text-amber-700",
    "Cold Chain": "bg-blue-50 border-blue-200 text-blue-700",
    Compliance: "bg-violet-50 border-violet-200 text-violet-700",
    CRM:        "bg-slate-50 border-slate-200 text-slate-600",
  };
  const c = cls[cat] ?? "bg-slate-50 border-slate-200 text-slate-600";
  return <span className={`inline-flex text-xs px-2 py-0.5 rounded-full border font-medium ${c}`}>{cat}</span>;
}

function SubmissionStatusBadge({ status }: { status: string }) {
  const cls =
    status === "Passed"       ? "bg-teal-50 border-teal-200 text-teal-700" :
    status === "Defect Found" ? "bg-red-50 border-red-200 text-red-700" :
    status === "Pending Review" ? "bg-amber-50 border-amber-200 text-amber-700" :
    "bg-slate-50 border-slate-200 text-slate-500";
  return <span className={`inline-flex text-xs px-2 py-0.5 rounded-full border font-medium ${cls}`}>{status}</span>;
}

// ── Fill Form Modal ───────────────────────────────────────────────────────────

// Per-form field schemas keyed by formKey — this is form configuration (the real
// questions each form asks), so "Proof of Delivery" no longer shows brake/tyre
// inspection questions. "check" renders Pass/Fail/N/A; text/number render inputs.
type FormFieldType = "check" | "text" | "number";
interface FormFieldDef { label: string; type: FormFieldType }

const INSPECTION_FIELDS: FormFieldDef[] = [
  { label: "Brakes functional?", type: "check" },
  { label: "Lights — headlights, indicators, hazards?", type: "check" },
  { label: "Tyres — pressure and condition?", type: "check" },
  { label: "Engine fluids — oil, coolant, washer?", type: "check" },
  { label: "Mirrors & windshield clear?", type: "check" },
  { label: "Horn & wipers working?", type: "check" },
  { label: "Odometer reading", type: "number" },
];

const FORM_FIELDS: Record<string, FormFieldDef[]> = {
  "pre-trip": INSPECTION_FIELDS,
  "post-trip": [
    { label: "New damage during trip?", type: "check" },
    { label: "Brakes & tyres condition OK?", type: "check" },
    { label: "Any fluid leaks observed?", type: "check" },
    { label: "Cargo area secure & clean?", type: "check" },
    { label: "Odometer reading", type: "number" },
  ],
  "dvir": INSPECTION_FIELDS,
  "incident": [
    { label: "Date & time of incident", type: "text" },
    { label: "Location", type: "text" },
    { label: "Injuries reported?", type: "check" },
    { label: "Third party involved?", type: "check" },
    { label: "Police notified?", type: "check" },
    { label: "Description of what happened", type: "text" },
  ],
  "delivery-pod": [
    { label: "Recipient name", type: "text" },
    { label: "Delivered in full?", type: "check" },
    { label: "Packages delivered (count)", type: "number" },
    { label: "Any damage on delivery?", type: "check" },
    { label: "Signature / photo captured?", type: "check" },
  ],
  "fuel-slip": [
    { label: "Fuel station / vendor", type: "text" },
    { label: "Gallons / litres", type: "number" },
    { label: "Total amount", type: "number" },
    { label: "Odometer at fill", type: "number" },
    { label: "Receipt attached?", type: "check" },
  ],
  "reefer-check": [
    { label: "Set point temperature (°C)", type: "number" },
    { label: "Actual return-air temperature (°C)", type: "number" },
    { label: "Unit running continuously?", type: "check" },
    { label: "Any alarm active?", type: "check" },
  ],
  "safety-obs": [
    { label: "Observation type (safe / at-risk)", type: "text" },
    { label: "Location / area", type: "text" },
    { label: "Immediate hazard present?", type: "check" },
    { label: "Description", type: "text" },
  ],
  "load-check": [
    { label: "Load within weight limit?", type: "check" },
    { label: "Straps / chains secured?", type: "check" },
    { label: "Weight distribution balanced?", type: "check" },
    { label: "Placards / labels correct?", type: "check" },
    { label: "Seal number", type: "text" },
  ],
  "customer-onboarding": [
    { label: "Company legal name", type: "text" },
    { label: "Primary contact", type: "text" },
    { label: "Billing email", type: "text" },
    { label: "Service address", type: "text" },
    { label: "Credit terms agreed?", type: "check" },
  ],
};

function FillFormModal({ template, onClose }: { template: AnyRecord; onClose: () => void }) {
  const qc = useQueryClient();
  const [answers, setAnswers] = useState<Record<string, string>>({});
  const [notes, setNotes] = useState("");

  const submitMut = useMutation({
    mutationFn: (data: AnyRecord) => apiClient.post("/api/digital-forms/submissions", data),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ["digital-forms", "submissions"] });
      onClose();
    },
  });

  const fields = FORM_FIELDS[String(template.formKey)] ?? INSPECTION_FIELDS;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40" onClick={onClose}>
      <div className="bg-white rounded-2xl w-full max-w-md mx-4 shadow-2xl" onClick={(e) => e.stopPropagation()}>
        <div className="px-6 py-4 border-b border-slate-100 flex items-center justify-between">
          <div>
            <p className="font-semibold text-slate-900">{String(template.title)}</p>
            <p className="text-xs text-slate-400">{fields.length} fields · {String(template.compliance ?? "")}</p>
          </div>
          <button type="button" onClick={onClose} className="text-slate-400 hover:text-slate-600">✕</button>
        </div>
        <div className="px-6 py-4 flex flex-col gap-3 max-h-80 overflow-y-auto">
          {fields.map((field) => (
            <div key={field.label} className="flex items-center justify-between gap-4">
              <label className="text-sm text-slate-700 flex-1">{field.label}</label>
              {field.type === "check" ? (
                <div className="flex gap-2">
                  {["Pass", "Fail", "N/A"].map((opt) => (
                    <button key={opt} type="button"
                      onClick={() => setAnswers((prev) => ({ ...prev, [field.label]: opt }))}
                      className={`px-2.5 py-1 text-xs rounded-lg border font-medium transition-colors ${
                        answers[field.label] === opt
                          ? opt === "Pass" ? "bg-teal-500 border-teal-500 text-white" : opt === "Fail" ? "bg-red-500 border-red-500 text-white" : "bg-slate-500 border-slate-500 text-white"
                          : "border-slate-200 text-slate-500 hover:bg-slate-50"
                      }`}>{opt}</button>
                  ))}
                </div>
              ) : (
                <input
                  type={field.type === "number" ? "number" : "text"}
                  value={answers[field.label] ?? ""}
                  onChange={(e) => setAnswers((prev) => ({ ...prev, [field.label]: e.target.value }))}
                  className="w-44 border border-slate-200 rounded-lg px-2.5 py-1 text-sm text-slate-900 focus:outline-none focus:ring-2 focus:ring-teal-400"
                />
              )}
            </div>
          ))}
          <div>
            <label className="text-xs text-slate-500 font-medium">Notes / Defect Description</label>
            <textarea rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} placeholder="Optional — describe any defects or observations"
              className="mt-1 w-full border border-slate-200 rounded-lg px-3 py-2 text-sm text-slate-900 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-teal-400 resize-none" />
          </div>
        </div>
        <div className="px-6 py-4 border-t border-slate-100 flex gap-3 justify-end">
          <button type="button" onClick={onClose} className="btn-secondary text-sm">Cancel</button>
          <button type="button" disabled={submitMut.isPending}
            onClick={() => submitMut.mutate({ formKey: template.formKey, answers, notes })}
            className="btn-primary text-sm disabled:opacity-50">
            {submitMut.isPending ? "Submitting…" : "Submit Form"}
          </button>
        </div>
      </div>
    </div>
  );
}

// ── Main page ─────────────────────────────────────────────────────────────────

type Tab = "templates" | "submissions";

export function DigitalFormsPage() {
  const [tab, setTab] = useState<Tab>("templates");
  const [filling, setFilling] = useState<AnyRecord | null>(null);
  const [catFilter, setCatFilter] = useState("All");

  const templatesQ = useQuery({ queryKey: ["digital-forms", "templates"],    queryFn: formsApi.templates });
  const submissionsQ = useQuery({ queryKey: ["digital-forms", "submissions"], queryFn: formsApi.submissions });

  const templates   = (templatesQ.data   ?? []) as AnyRecord[];
  const submissions = (submissionsQ.data ?? []) as AnyRecord[];

  const activeTemplates  = templates.filter((t) => t.active);
  const defects = submissions.filter((s) => Number(s.defects) > 0).length;
  const complianceRate = submissions.length > 0 ? Math.round(submissions.filter((s) => s.status !== "Defect Found").length / submissions.length * 100) : 0;

  const cats = ["All", ...Array.from(new Set(templates.map((t) => String(t.category ?? ""))))];
  const filteredTemplates = catFilter === "All" ? templates : templates.filter((t) => t.category === catFilter);

  if (templatesQ.isLoading) return <LoadingState />;

  return (
    <div className="flex h-full flex-col gap-6 overflow-y-auto py-6">
      <div className="flex items-start justify-between gap-4 flex-wrap">
        <div>
          <h1 className="text-xl font-bold text-slate-900">Digital Forms</h1>
          <p className="text-sm text-slate-500 mt-0.5">Pre-trip, DVIR, incident, delivery and compliance digital checklists — fill, submit and track compliance</p>
        </div>
        <button type="button" className="btn-secondary text-sm"
          onClick={() => exportCsv("form-submissions", submissions)}>Export Submissions</button>
      </div>

      {/* KPI strip */}
      <div className="flex flex-wrap gap-3">
        {[
          { label: "Active Forms",      val: activeTemplates.length },
          { label: "Submissions (30d)", val: submissions.length,    accent: "text-teal-600" },
          { label: "Compliance Rate",   val: `${complianceRate}%`,  accent: complianceRate >= 90 ? "text-teal-600" : "text-amber-600" },
          { label: "Defects Found",     val: defects,               accent: defects > 0 ? "text-red-600" : "text-teal-600" },
        ].map(({ label, val, accent }) => (
          <div key={label} className="panel flex flex-col gap-1 min-w-28">
            <span className={`text-xl font-bold ${accent ?? "text-slate-900"}`}>{val}</span>
            <span className="text-xs text-slate-500 font-medium">{label}</span>
          </div>
        ))}
      </div>

      {/* Tabs */}
      <div className="panel flex gap-1 p-1.5">
        {(["templates", "submissions"] as Tab[]).map((t) => (
          <button key={t} type="button" onClick={() => setTab(t)}
            className={`px-4 py-2 rounded-lg text-sm font-medium capitalize transition-colors ${
              tab === t ? "bg-teal-600 text-white shadow-sm" : "text-slate-600 hover:bg-slate-100"
            }`}>{t === "templates" ? "Form Library" : "Submission History"}</button>
        ))}
      </div>

      {/* ── Form Library ── */}
      {tab === "templates" && (
        <div className="flex flex-col gap-4">
          <div className="flex gap-1.5 flex-wrap">
            {cats.map((c) => (
              <button key={c} type="button" onClick={() => setCatFilter(c)}
                className={`px-3 py-1.5 rounded-lg text-sm font-medium border transition-colors ${
                  catFilter === c ? "bg-teal-50 border-teal-300 text-teal-700" : "bg-slate-50 border-slate-200 text-slate-600 hover:bg-slate-100"
                }`}>{c}</button>
            ))}
          </div>
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
            {filteredTemplates.map((tmpl) => (
              <div key={String(tmpl.formKey)} className={`panel flex flex-col gap-3 ${!tmpl.active ? "opacity-60" : ""}`}>
                <div className="flex items-start justify-between gap-2">
                  <div className="flex-1 min-w-0">
                    <p className="font-semibold text-slate-900 text-sm">{String(tmpl.title)}</p>
                    <div className="mt-1.5 flex flex-wrap gap-1">
                      <CategoryBadge cat={String(tmpl.category ?? "")} />
                      {!tmpl.active && <span className="text-xs px-2 py-0.5 rounded-full border border-slate-200 text-slate-400 bg-slate-50">Inactive</span>}
                    </div>
                  </div>
                </div>
                <div className="grid grid-cols-2 gap-2 text-xs">
                  <div>
                    <p className="text-slate-400">Fields</p>
                    <p className="font-semibold text-slate-700">{String(tmpl.fields)}</p>
                  </div>
                  <div>
                    <p className="text-slate-400">Frequency</p>
                    <p className="font-semibold text-slate-700">{String(tmpl.frequency ?? "—")}</p>
                  </div>
                  <div className="col-span-2">
                    <p className="text-slate-400">Compliance</p>
                    <p className="font-medium text-slate-600 truncate">{String(tmpl.compliance ?? "—")}</p>
                  </div>
                </div>
                <div className="flex items-center justify-between pt-1 border-t border-slate-100">
                  <span className="text-xs text-slate-400">Role: {String(tmpl.requiredRole ?? "Any")}</span>
                  <button type="button" disabled={!tmpl.active}
                    onClick={() => setFilling(tmpl)}
                    className="text-xs px-3 py-1.5 rounded-lg bg-teal-50 border border-teal-300 text-teal-700 hover:bg-teal-100 transition-colors disabled:opacity-40 disabled:cursor-not-allowed font-medium">
                    Fill Form →
                  </button>
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* ── Submission History ── */}
      {tab === "submissions" && (
        submissions.length === 0 ? <EmptyState title="No submissions yet" /> : (
          <div className="panel overflow-hidden p-0">
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-slate-200 bg-slate-50">
                    {["Form", "Submitted By", "Vehicle", "Date / Time", "Status", "Defects", "Attachments"].map((h) => (
                      <th key={h} className="text-left px-4 py-3 text-xs font-semibold text-slate-500 uppercase tracking-wide">{h}</th>
                    ))}
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {submissions.map((s, i) => (
                    <tr key={String(s.id ?? i)} className="hover:bg-slate-50">
                      <td className="px-4 py-3 font-medium text-slate-900 max-w-48 truncate">{String(s.formTitle ?? "—")}</td>
                      <td className="px-4 py-3 text-slate-700">{String(s.submittedBy ?? "—")}</td>
                      <td className="px-4 py-3 text-slate-600 text-xs">{String(s.vehicleCode ?? "—")}</td>
                      <td className="px-4 py-3 text-xs text-slate-500">{String(s.submittedAt ?? "—")}</td>
                      <td className="px-4 py-3"><SubmissionStatusBadge status={String(s.status ?? "Passed")} /></td>
                      <td className="px-4 py-3">
                        {Number(s.defects) > 0
                          ? <span className="text-xs font-medium text-red-700 bg-red-50 border border-red-200 px-1.5 py-0.5 rounded">{String(s.defects)} defect</span>
                          : <span className="text-xs text-slate-400">—</span>
                        }
                      </td>
                      <td className="px-4 py-3 text-xs text-slate-500">{Number(s.attachments) > 0 ? `${String(s.attachments)} file` : "—"}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )
      )}

      {/* Fill form modal */}
      {filling && <FillFormModal template={filling} onClose={() => setFilling(null)} />}
    </div>
  );
}
