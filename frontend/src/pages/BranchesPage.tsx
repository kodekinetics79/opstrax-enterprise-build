import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Pencil, Plus, Search, X } from "lucide-react";
import { PageHeader, EmptyState, ErrorState, LoadingState, StatusBadge } from "@/components/ui";
import { useHasPermission } from "@/hooks/usePermission";
import { branchesApi } from "@/services/branchesApi";
import type { AnyRecord } from "@/types";

type BranchForm = {
  id?: number;
  branchCode: string;
  name: string;
  branchType: "branch" | "depot" | "yard";
  region: string;
  city: string;
  state: string;
  countryCode: string;
  timezone: string;
  status: string;
};

const emptyForm: BranchForm = {
  branchCode: "", name: "", branchType: "branch", region: "", city: "", state: "",
  countryCode: "US", timezone: "America/New_York", status: "Active",
};

function apiError(error: unknown) {
  const data = (error as { response?: { data?: { message?: string; errors?: string[] } } })?.response?.data;
  return data?.errors?.[0] ?? data?.message ?? "The branch could not be saved.";
}

export function BranchesPage() {
  const queryClient = useQueryClient();
  const hasPermission = useHasPermission();
  const canManage = hasPermission("fleet:manage");
  const [search, setSearch] = useState("");
  const [form, setForm] = useState<BranchForm | null>(null);
  const [error, setError] = useState<string | null>(null);
  const branchesQ = useQuery({ queryKey: ["branches"], queryFn: branchesApi.list });
  const save = useMutation({
    mutationFn: (value: BranchForm) => value.id
      ? branchesApi.update(value.id, value)
      : branchesApi.create(value),
    onSuccess: async () => {
      setForm(null);
      await queryClient.invalidateQueries({ queryKey: ["branches"] });
    },
    onError: (reason) => setError(apiError(reason)),
  });
  const rows = useMemo(() => {
    const needle = search.trim().toLowerCase();
    return (branchesQ.data ?? []).filter((row) => !needle ||
      [row.branchCode, row.name, row.branchType, row.city, row.region].some((value) => String(value ?? "").toLowerCase().includes(needle)));
  }, [branchesQ.data, search]);

  if (branchesQ.isLoading) return <LoadingState />;
  if (branchesQ.isError) return <ErrorState message="Unable to load branch ownership." />;

  return (
    <div className="space-y-4">
      <PageHeader
        eyebrow="Fleet identity"
        title="Branches"
        description="Create and maintain the branch, depot, and yard ownership scopes used by fleet records and role accounts."
        actions={<button className="btn-primary" disabled={!canManage} onClick={() => { setError(null); setForm({ ...emptyForm }); }}><Plus className="h-4 w-4" /> Add Branch</button>}
      />
      <div className="panel p-4">
        <label className="relative block max-w-lg">
          <Search className="pointer-events-none absolute left-3 top-3 h-4 w-4 text-slate-400" />
          <input className="field w-full pl-9" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Search code, name, type, city or region" />
        </label>
      </div>
      {rows.length === 0 ? <EmptyState title="No branches found" subtitle="Create a branch to establish fleet ownership scopes." /> : (
        <div className="panel overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead><tr className="border-b border-slate-200 text-xs uppercase tracking-wide text-slate-500">
              {['Code','Name','Type','Location','Vehicles','Drivers','Status',''].map((label) => <th key={label} className="px-4 py-3">{label}</th>)}
            </tr></thead>
            <tbody className="divide-y divide-slate-100">{rows.map((row) => (
              <tr key={String(row.id)}>
                <td className="px-4 py-3 font-mono font-semibold">{String(row.branchCode ?? "")}</td>
                <td className="px-4 py-3 font-semibold">{String(row.name ?? "")}</td>
                <td className="px-4 py-3 capitalize">{String(row.branchType ?? "branch")}</td>
                <td className="px-4 py-3">{[row.city, row.state, row.region].filter(Boolean).join(", ") || "—"}</td>
                <td className="px-4 py-3 tabular-nums">{Number(row.vehicleCount ?? 0)}</td>
                <td className="px-4 py-3 tabular-nums">{Number(row.driverCount ?? 0)}</td>
                <td className="px-4 py-3"><StatusBadge status={String(row.status ?? "Active")} /></td>
                <td className="px-4 py-3"><button className="icon-btn" disabled={!canManage} aria-label={`Edit ${String(row.name)}`} onClick={() => { setError(null); setForm({
                  id: Number(row.id), branchCode: String(row.branchCode ?? ""), name: String(row.name ?? ""),
                  branchType: (String(row.branchType ?? "branch") as BranchForm['branchType']), region: String(row.region ?? ""),
                  city: String(row.city ?? ""), state: String(row.state ?? ""), countryCode: String(row.countryCode ?? "US"),
                  timezone: String(row.timezone ?? "America/New_York"), status: String(row.status ?? "Active"),
                }); }}><Pencil className="h-4 w-4" /></button></td>
              </tr>
            ))}</tbody>
          </table>
        </div>
      )}
      {form && (
        <div className="fixed inset-0 z-50 grid place-items-center bg-slate-900/35 p-4 backdrop-blur-sm">
          <div className="panel w-full max-w-2xl space-y-4 p-6" role="dialog" aria-label={form.id ? "Edit branch" : "Add branch"}>
            <div className="flex items-center justify-between"><h2 className="text-lg font-bold">{form.id ? "Edit Branch" : "Add Branch"}</h2><button className="icon-btn" onClick={() => setForm(null)}><X className="h-4 w-4" /></button></div>
            <div className="grid gap-3 md:grid-cols-2">
              <div><label className="label">Branch code</label><input className="field w-full" disabled={Boolean(form.id)} value={form.branchCode} onChange={(e) => setForm({ ...form, branchCode: e.target.value.toUpperCase() })} /></div>
              <div><label className="label">Name</label><input className="field w-full" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} /></div>
              <div><label className="label">Type</label><select className="field w-full" value={form.branchType} onChange={(e) => setForm({ ...form, branchType: e.target.value as BranchForm['branchType'] })}><option value="branch">Branch</option><option value="depot">Depot</option><option value="yard">Yard</option></select></div>
              <div><label className="label">Region</label><input className="field w-full" value={form.region} onChange={(e) => setForm({ ...form, region: e.target.value })} /></div>
              <div><label className="label">City</label><input className="field w-full" value={form.city} onChange={(e) => setForm({ ...form, city: e.target.value })} /></div>
              <div><label className="label">State / Province</label><input className="field w-full" value={form.state} onChange={(e) => setForm({ ...form, state: e.target.value })} /></div>
              <div><label className="label">Country code</label><input className="field w-full" maxLength={2} value={form.countryCode} onChange={(e) => setForm({ ...form, countryCode: e.target.value.toUpperCase() })} /></div>
              <div><label className="label">Timezone</label><input className="field w-full" value={form.timezone} onChange={(e) => setForm({ ...form, timezone: e.target.value })} /></div>
              {form.id && <div><label className="label">Status</label><select className="field w-full" value={form.status} onChange={(e) => setForm({ ...form, status: e.target.value })}><option>Active</option><option>Inactive</option></select></div>}
            </div>
            {error && <p role="alert" className="rounded-lg border border-rose-200 bg-rose-50 p-3 text-sm text-rose-700">{error}</p>}
            <div className="flex justify-end gap-2"><button className="btn-ghost" onClick={() => setForm(null)}>Cancel</button><button className="btn-primary" disabled={save.isPending || !form.branchCode.trim() || !form.name.trim()} onClick={() => { setError(null); save.mutate(form); }}>{save.isPending ? "Saving…" : "Save Branch"}</button></div>
          </div>
        </div>
      )}
    </div>
  );
}
