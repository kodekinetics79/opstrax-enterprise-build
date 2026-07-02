import { FormEvent, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Bot, Plus, RadioTower, ShieldCheck, Target, X } from "lucide-react";
import { modules, moduleIcons } from "@/modules/moduleConfig";
import { AiInsightCard, DataTable, DetailDrawer, FilterBar, KpiCard, LoadingState, PageHeader } from "@/components/ui";
import { modulesApi } from "@/services/modulesApi";
import type { AnyRecord } from "@/types";

const CREATE_FIELDS: { key: string; label: string; type?: string }[] = [
  { key: "title",        label: "Title" },
  { key: "status",       label: "Status" },
  { key: "ownerName",    label: "Owner" },
  { key: "locationName", label: "Location" },
  { key: "riskLevel",    label: "Risk Level" },
  { key: "amount",       label: "Amount",  type: "number" },
  { key: "dueAt",        label: "Due Date", type: "date" },
];

function CreateModal({ moduleTitle, saving, onClose, onSave }: { moduleTitle: string; saving: boolean; onClose: () => void; onSave: (payload: AnyRecord) => void }) {
  const [form, setForm] = useState<AnyRecord>({ status: "Active", riskLevel: "Low" });
  const submit = (e: FormEvent) => { e.preventDefault(); onSave(form); };
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-sm p-4">
      <div className="w-full max-w-lg rounded-2xl border border-slate-200 bg-white shadow-xl">
        <div className="flex items-center justify-between border-b border-slate-100 px-6 py-4">
          <h2 className="text-base font-bold text-slate-900">New {moduleTitle} Record</h2>
          <button type="button" aria-label="Close" className="icon-btn" onClick={onClose}><X className="h-4 w-4" /></button>
        </div>
        <form onSubmit={submit} className="space-y-4 px-6 py-5">
          {CREATE_FIELDS.map(({ key, label, type }) => (
            <div key={key} className="space-y-1">
              <label htmlFor={`create-${key}`} className="text-xs font-semibold text-slate-600 uppercase tracking-wide">{label}</label>
              <input
                id={`create-${key}`}
                className="field"
                type={type ?? "text"}
                value={String(form[key] ?? "")}
                onChange={(e) => setForm((prev) => ({ ...prev, [key]: e.target.value }))}
                required={key === "title"}
              />
            </div>
          ))}
          <div className="flex justify-end gap-3 pt-2">
            <button type="button" className="btn-ghost" onClick={onClose}>Cancel</button>
            <button type="submit" className="btn-primary" disabled={saving}>
              {saving ? "Saving…" : "Create Record"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

export function ModulePage({ moduleKey }: { moduleKey: string }) {
  const [selected, setSelected] = useState<AnyRecord | null>(null);
  const [creating, setCreating] = useState(false);
  const [reviewOnly, setReviewOnly] = useState(false);
  const module = modules.find((item) => item.key === moduleKey)!;
  const Icon = moduleIcons[moduleKey] || RadioTower;
  const qc = useQueryClient();

  const query = useQuery({ queryKey: ["module", moduleKey], queryFn: () => modulesApi.get(moduleKey) });
  const columns = useMemo(() => ["title", "status", "ownerName", "locationName", "riskLevel", "amount", "dueAt"], []);

  const create = useMutation({
    mutationFn: (payload: AnyRecord) => modulesApi.create(moduleKey, payload),
    onSuccess: async () => { setCreating(false); await qc.invalidateQueries({ queryKey: ["module", moduleKey] }); },
  });

  if (query.isLoading) return <LoadingState />;
  const records = query.data?.records || [];
  const displayRecords = reviewOnly
    ? records.filter((r) => /high|critical/i.test(String(r.riskLevel ?? "")))
    : records;

  return (
    <div className="flex h-full flex-col gap-6 overflow-y-auto">
      <PageHeader
        eyebrow={module.group}
        title={module.title}
        description={module.description}
        actions={
          <>
            <button className="btn-primary" onClick={() => setCreating(true)}>
              <Plus className="h-4 w-4" />
              Create
            </button>
            <button
              className={reviewOnly ? "btn-primary" : "btn-ghost"}
              onClick={() => setReviewOnly(!reviewOnly)}
              title="Show only high/critical risk records"
            >
              {reviewOnly ? "All Records" : "Review Queue"}
            </button>
          </>
        }
      />
      <div className="grid gap-4 md:grid-cols-4">
        <KpiCard label="Records" value={records.length} icon={<Icon />} status="Active" />
        <KpiCard label="Open / Active" value={String(query.data?.summary?.active ?? records.filter((x) => String(x.status).match(/open|active|progress/i)).length)} icon={<ShieldCheck />} status="Healthy" />
        <KpiCard label="Risk Items" value={String(query.data?.summary?.riskItems ?? records.filter((x) => String(x.riskLevel).match(/high|critical/i)).length)} icon={<Target />} status="Review" />
        <KpiCard label="Fleet Insights" value={query.data?.insights?.length || 0} icon={<Bot />} status="Recommended" />
      </div>
      <FilterBar />
      <div className="grid gap-6 xl:grid-cols-[1fr_360px]">
        <DataTable rows={displayRecords} columns={columns} onSelect={setSelected} />
        <div className="space-y-4">
          {(query.data?.insights || []).slice(0, 3).map((insight) => <AiInsightCard key={String(insight.id)} insight={insight} />)}
          {!query.data?.insights?.length ? <AiInsightCard insight={{ title: `${module.title} insights`, body: "Operational recommendations will surface as live events and data flow through this module." }} /> : null}
        </div>
      </div>
      <DetailDrawer record={selected} onClose={() => setSelected(null)} />
      {creating && (
        <CreateModal
          moduleTitle={module.title}
          saving={create.isPending}
          onClose={() => setCreating(false)}
          onSave={(p) => create.mutate(p)}
        />
      )}
    </div>
  );
}
