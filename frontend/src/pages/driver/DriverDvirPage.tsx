import { useState, useMemo } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { CheckCircle, ClipboardList, XCircle } from "lucide-react";
import { driverApi } from "@/services/driverApi";
import { ErrorState } from "@/components/ui";
import type { AnyRecord } from "@/types";

type ChecklistResult = "pass" | "fail" | "na";

interface ChecklistItemState {
  templateId: number;
  itemName: string;
  category: string;
  isRequired: boolean;
  result: ChecklistResult | null;
  notes: string;
  severity: string;
}

function ResultButton({ label, active, color, onClick }: {
  label: string; active: boolean; color: string; onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={`flex-1 rounded-xl py-3 text-sm font-bold transition ${
        active ? color : "bg-slate-100 text-slate-400"
      }`}
    >
      {label}
    </button>
  );
}

export function DriverDvirPage() {
  const qc = useQueryClient();
  const [selectedTemplateId, setSelectedTemplateId] = useState<number | null>(null);
  const [vehicleId, setVehicleId] = useState("");
  const [inspectionType, setInspectionType] = useState("pre_trip");
  const [odometer, setOdometer] = useState("");
  const [notes, setNotes] = useState("");
  const [items, setItems] = useState<ChecklistItemState[]>([]);
  const [submitted, setSubmitted] = useState(false);

  const templates = useQuery<AnyRecord[]>({
    queryKey: ["driver", "dvir-templates"],
    queryFn: driverApi.dvirTemplates,
  });

  // The driver's own vehicles — a real picker instead of asking a driver to type a numeric
  // vehicle id (a primary key they have no way of knowing).
  //
  // Sourced from BOTH the driver's permanently-assigned vehicle (/driver/me) and any vehicle
  // on an active assignment. Assignments alone are not enough: a pre-trip inspection happens
  // BEFORE accepting a load, which is the single most common DVIR in a real fleet, and a
  // driver with no active assignment was dropped to a free-text "Vehicle ID" box — a dead end.
  const meQ = useQuery<AnyRecord>({ queryKey: ["driver", "me"], queryFn: driverApi.me });
  const assignmentsQ = useQuery<AnyRecord[]>({ queryKey: ["driver", "assignments"], queryFn: driverApi.assignments });
  const vehicles = useMemo(() => {
    const map = new Map<string, string>();

    const me = meQ.data as AnyRecord | undefined;
    const driver = (me?.["driver"] ?? me) as AnyRecord | undefined;
    const myVid = driver?.["vehicleId"] ?? driver?.["vehicle_id"];
    if (myVid != null && myVid !== "") {
      map.set(String(myVid), String(driver?.["vehicleCode"] ?? driver?.["vehicle_code"] ?? myVid));
    }

    for (const a of assignmentsQ.data ?? []) {
      const vid = a["vehicleId"] ?? a["vehicle_id"];
      if (vid != null && vid !== "") map.set(String(vid), String(a["vehicleCode"] ?? a["vehicle_code"] ?? vid));
    }
    return [...map.entries()].map(([id, code]) => ({ id, code }));
  }, [meQ.data, assignmentsQ.data]);

  const submitMut = useMutation({
    mutationFn: driverApi.submitDvir,
    onSuccess: () => {
      setSubmitted(true);
      void qc.invalidateQueries({ queryKey: ["driver"] });
    },
  });

  function selectTemplate(id: number) {
    setSelectedTemplateId(id);
    const tmpl = (templates.data ?? []).find(t => Number(t["id"]) === id);
    if (!tmpl) return;
    const checklistItems = ((tmpl["items"] as AnyRecord[]) ?? []).map(ci => ({
      templateId: id,
      itemName:   String(ci["itemName"] ?? ""),
      category:   String(ci["itemCategory"] ?? "General"),
      isRequired: Boolean(ci["isRequired"]),
      result:     null as ChecklistResult | null,
      notes:      "",
      severity:   "Low",
    }));
    setItems(checklistItems);
  }

  function setResult(idx: number, result: ChecklistResult) {
    setItems(prev => prev.map((item, i) => i === idx ? { ...item, result } : item));
  }

  function setItemNotes(idx: number, n: string) {
    setItems(prev => prev.map((item, i) => i === idx ? { ...item, notes: n } : item));
  }

  function setItemSeverity(idx: number, s: string) {
    setItems(prev => prev.map((item, i) => i === idx ? { ...item, severity: s } : item));
  }

  const hasFail   = items.some(i => i.result === "fail");
  const allDone   = items.length > 0 && items.every(i => i.result !== null);
  const incompleteRequired = items.some(i => i.isRequired && i.result === null);

  if (submitted) {
    return (
      <div className="flex flex-col items-center justify-center gap-4 p-12 text-center">
        <CheckCircle className="h-14 w-14 text-teal-500" />
        <p className="text-lg font-bold text-slate-900">DVIR Submitted</p>
        {hasFail && (
          <p className="text-sm text-amber-700 font-medium">
            Defects recorded. Maintenance has been notified.
          </p>
        )}
        <button
          type="button"
          className="mt-4 rounded-2xl bg-teal-600 px-6 py-3 text-sm font-bold text-white"
          onClick={() => { setSubmitted(false); setSelectedTemplateId(null); setItems([]); }}
        >
          Start Another Inspection
        </button>
      </div>
    );
  }

  if (templates.isLoading) {
    return (
      <div className="flex flex-col items-center justify-center gap-4 p-12">
        <ClipboardList className="h-12 w-12 animate-pulse text-slate-300" />
        <p className="text-sm text-slate-500">Loading DVIR templates…</p>
      </div>
    );
  }

  if (templates.isError) return <ErrorState message={(templates.error as Error)?.message} />;

  return (
    <div className="space-y-4 p-4 pb-10">
      <div className="pt-2">
        <p className="text-xs font-bold uppercase tracking-widest text-slate-400">Vehicle Inspection</p>
        <h1 className="mt-1 text-xl font-bold text-slate-900">DVIR</h1>
      </div>

      {/* Setup */}
      {!selectedTemplateId ? (
        <div className="space-y-4">
          <div>
            <label className="text-xs font-bold text-slate-500 block mb-1">Vehicle</label>
            {vehicles.length > 0 ? (
              <select
                className="w-full rounded-xl border border-slate-200 px-4 py-3 text-sm"
                value={vehicleId}
                onChange={e => setVehicleId(e.target.value)}
              >
                <option value="">Select vehicle…</option>
                {vehicles.map(v => <option key={v.id} value={v.id}>{v.code}</option>)}
              </select>
            ) : (
              <input
                type="number"
                className="w-full rounded-xl border border-slate-200 px-4 py-3 text-sm"
                placeholder={assignmentsQ.isLoading ? "Loading your vehicles…" : "Vehicle ID (no assigned vehicles found)"}
                value={vehicleId}
                onChange={e => setVehicleId(e.target.value)}
              />
            )}
          </div>
          <div>
            <label className="text-xs font-bold text-slate-500 block mb-1">Inspection Type</label>
            <select
              className="w-full rounded-xl border border-slate-200 px-4 py-3 text-sm"
              value={inspectionType}
              onChange={e => setInspectionType(e.target.value)}
            >
              <option value="pre_trip">Pre-trip</option>
              <option value="post_trip">Post-trip</option>
              <option value="en_route">En Route</option>
            </select>
          </div>
          <div>
            <label className="text-xs font-bold text-slate-500 block mb-1">Odometer (mi)</label>
            <input
              type="number"
              className="w-full rounded-xl border border-slate-200 px-4 py-3 text-sm"
              placeholder="Optional"
              value={odometer}
              onChange={e => setOdometer(e.target.value)}
            />
          </div>

          {/* Template selection */}
          <div>
            <p className="text-xs font-bold text-slate-500 mb-2">Select Checklist</p>
            {!vehicleId && <p className="mb-2 text-xs font-medium text-amber-600">Select a vehicle above to start an inspection.</p>}
            {(templates.data ?? []).map(t => (
              <button
                key={String(t["id"])}
                type="button"
                disabled={!vehicleId}
                className="mb-2 flex w-full items-center justify-between rounded-2xl border border-slate-200 bg-white px-5 py-4 text-left text-sm font-semibold text-slate-800 shadow-sm active:bg-slate-50 disabled:opacity-40 disabled:cursor-not-allowed"
                onClick={() => selectTemplate(Number(t["id"]))}
              >
                <span>{String(t["templateName"] ?? t["id"])}</span>
                <span className="text-xs text-slate-400">{String(t["inspectionType"] ?? "")}</span>
              </button>
            ))}
            {(templates.data ?? []).length === 0 && (
              <p className="text-sm text-slate-400">No inspection templates available. Contact your fleet admin.</p>
            )}
          </div>
        </div>
      ) : (
        /* Checklist form */
        <div className="space-y-4">
          {/* Group by category */}
          {Array.from(new Set(items.map(i => i.category))).map(cat => (
            <div key={cat} className="rounded-2xl border border-slate-200 bg-white overflow-hidden">
              <div className="bg-slate-50 px-4 py-2 border-b border-slate-200">
                <p className="text-xs font-bold uppercase tracking-wider text-slate-500">{cat}</p>
              </div>
              {items.filter(i => i.category === cat).map((item, _) => {
                const globalIdx = items.indexOf(item);
                return (
                  <div key={globalIdx} className="border-b border-slate-100 last:border-none px-4 py-4 space-y-2">
                    <p className="text-sm font-semibold text-slate-800">
                      {item.itemName}
                      {item.isRequired && <span className="ml-1 text-red-500">*</span>}
                    </p>
                    <div className="flex gap-2">
                      <ResultButton
                        label="Pass"
                        active={item.result === "pass"}
                        color="bg-teal-500 text-white"
                        onClick={() => setResult(globalIdx, "pass")}
                      />
                      <ResultButton
                        label="Fail"
                        active={item.result === "fail"}
                        color="bg-red-500 text-white"
                        onClick={() => setResult(globalIdx, "fail")}
                      />
                      <ResultButton
                        label="N/A"
                        active={item.result === "na"}
                        color="bg-slate-500 text-white"
                        onClick={() => setResult(globalIdx, "na")}
                      />
                    </div>
                    {item.result === "fail" && (
                      <div className="space-y-2">
                        <select
                          className="w-full rounded-xl border border-red-200 px-3 py-2 text-sm"
                          value={item.severity}
                          onChange={e => setItemSeverity(globalIdx, e.target.value)}
                        >
                          <option value="Low">Low — advisory, can continue</option>
                          <option value="Medium">Medium — monitor closely</option>
                          <option value="High">High — fix at next stop</option>
                          <option value="Critical">Critical — out of service immediately</option>
                        </select>
                        <textarea
                          className="w-full rounded-xl border border-red-200 px-3 py-2 text-sm"
                          rows={2}
                          placeholder="Describe the defect…"
                          value={item.notes}
                          onChange={e => setItemNotes(globalIdx, e.target.value)}
                        />
                      </div>
                    )}
                  </div>
                );
              })}
            </div>
          ))}

          {/* General notes */}
          <div>
            <label className="text-xs font-bold text-slate-500 block mb-1">Additional Notes</label>
            <textarea
              className="w-full rounded-xl border border-slate-200 px-4 py-3 text-sm"
              rows={3}
              placeholder="Optional inspection notes…"
              value={notes}
              onChange={e => setNotes(e.target.value)}
            />
          </div>

          {hasFail && (
            <div className="rounded-2xl border border-red-300 bg-red-50 p-4">
              <div className="flex items-center gap-2">
                <XCircle className="h-5 w-5 text-red-600 shrink-0" />
                <p className="text-sm font-bold text-red-800">Defects recorded</p>
              </div>
              {items.some(i => i.result === "fail" && i.severity === "Critical") && (
                <p className="mt-1 text-xs text-red-700">One or more Critical defects will place the vehicle out of service.</p>
              )}
            </div>
          )}

          {incompleteRequired && (
            <p className="text-xs text-amber-600 font-medium">Complete all required items (*) before submitting.</p>
          )}

          <div className="flex gap-3">
            <button
              type="button"
              className="flex-1 rounded-2xl border border-slate-200 bg-white py-4 text-sm font-bold text-slate-600"
              onClick={() => setSelectedTemplateId(null)}
            >
              Back
            </button>
            <button
              type="button"
              disabled={submitMut.isPending || incompleteRequired || !allDone}
              className="flex-1 rounded-2xl bg-teal-600 py-4 text-sm font-bold text-white disabled:opacity-40"
              onClick={() => {
                void submitMut.mutateAsync({
                  vehicleId: Number(vehicleId),
                  inspectionType,
                  odometerMiles: odometer ? Number(odometer) : undefined,
                  notes: notes || undefined,
                  checklistItems: items.map(i => ({
                    category:  i.category,
                    itemName:  i.itemName,
                    result:    i.result as "pass" | "fail" | "na",
                    severity:  i.result === "fail" ? i.severity : undefined,
                    notes:     i.result === "fail" ? i.notes : undefined,
                  })),
                });
              }}
            >
              {submitMut.isPending ? "Submitting…" : "Submit DVIR"}
            </button>
          </div>

          {submitMut.isError && (
            <p className="text-sm text-red-600">{(submitMut.error as Error)?.message}</p>
          )}
        </div>
      )}
    </div>
  );
}
