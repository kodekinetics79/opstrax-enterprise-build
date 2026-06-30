import { useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  AlertTriangle, CheckCircle, Clock, ExternalLink, Eye, EyeOff,
  MapPin, MessageSquare, Package, Send, Share2, ShieldCheck, Star, Truck, XCircle,
} from "lucide-react";
import {
  AiInsightCard, EmptyState, ErrorState, exportCsv, KpiCard,
  LoadingState, PageHeader, StatusBadge,
} from "@/components/ui";
import { useHasPermission } from "@/hooks/usePermission";
import { customerVisibilityApi } from "@/services/customerVisibilityApi";
import { customerEtaApi } from "@/services/customerEtaApi";
import type { AnyRecord } from "@/types";

const TABS = ["Dashboard", "Shipment Detail", "Token Management", "Feedback"] as const;
type Tab = (typeof TABS)[number];

// ── Risk badge ───────────────────────────────────────────────────────────────
function EtaRiskBadge({ risk }: { risk?: unknown }) {
  const r = String(risk ?? "unknown").toLowerCase();
  const cfg: Record<string, string> = {
    on_time: "bg-teal-100 text-teal-800",
    at_risk: "bg-amber-100 text-amber-800",
    delayed: "bg-red-100 text-red-800",
    unknown: "bg-slate-100 text-slate-500",
  };
  const label: Record<string, string> = {
    on_time: "On Time", at_risk: "At Risk", delayed: "Delayed", unknown: "Unknown",
  };
  return (
    <span className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-semibold ${cfg[r] ?? cfg["unknown"]}`}>
      {r === "on_time" ? <CheckCircle className="h-3 w-3" /> : r === "delayed" ? <XCircle className="h-3 w-3" /> : <AlertTriangle className="h-3 w-3" />}
      {label[r] ?? "Unknown"}
    </span>
  );
}

function ConfidencePill({ confidence }: { confidence?: unknown }) {
  const c = String(confidence ?? "low").toLowerCase();
  const cfg: Record<string, string> = {
    high: "text-teal-700 bg-teal-50 border-teal-200",
    medium: "text-amber-700 bg-amber-50 border-amber-200",
    low: "text-red-700 bg-red-50 border-red-200",
    unknown: "text-slate-500 bg-slate-50 border-slate-200",
  };
  return (
    <span className={`inline-block rounded border px-1.5 py-0.5 text-xs font-medium ${cfg[c] ?? cfg["unknown"]}`}>
      {c.charAt(0).toUpperCase() + c.slice(1)} confidence
    </span>
  );
}

// ── Timeline item ────────────────────────────────────────────────────────────
function TimelineItem({ event, last }: { event: AnyRecord; last: boolean }) {
  const cat = String(event["category"] ?? "shipment");
  const iconCls = cat === "exception" ? "bg-amber-500" : cat === "proof" ? "bg-teal-500" : "bg-blue-500";
  return (
    <div className="relative flex gap-3">
      {!last && <div className="absolute left-3.5 top-7 bottom-0 w-px bg-slate-200" />}
      <div className={`mt-0.5 flex h-7 w-7 shrink-0 items-center justify-center rounded-full text-white ${iconCls}`}>
        {cat === "proof" ? <CheckCircle className="h-4 w-4" /> : cat === "exception" ? <AlertTriangle className="h-4 w-4" /> : <Package className="h-4 w-4" />}
      </div>
      <div className="pb-4">
        <p className="text-sm font-semibold text-slate-900">{String(event["eventType"] ?? "Event")}</p>
        {event["message"] != null ? <p className="mt-0.5 text-xs text-slate-500">{String(event["message"])}</p> : null}
        <p className="mt-0.5 text-xs text-slate-400">
          {event["occurredAt"] != null ? new Date(String(event["occurredAt"])).toLocaleString() : "--"}
        </p>
      </div>
    </div>
  );
}

// ── ETA card ─────────────────────────────────────────────────────────────────
function EtaCard({ eta }: { eta: AnyRecord }) {
  return (
    <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
      <p className="text-xs font-bold uppercase tracking-widest text-slate-400">ETA / Risk</p>
      <div className="mt-2 flex items-center gap-3">
        <Clock className="h-5 w-5 text-slate-400" />
        <span className="text-lg font-bold text-slate-900">
          {eta["etaAt"] != null ? new Date(String(eta["etaAt"])).toLocaleString() : "--"}
        </span>
      </div>
      <div className="mt-2 flex flex-wrap gap-2">
        <EtaRiskBadge risk={eta["risk"]} />
        <ConfidencePill confidence={eta["confidence"]} />
      </div>
      {eta["explanation"] != null ? (
        <p className="mt-3 text-xs text-slate-500">{String(eta["explanation"])}</p>
      ) : null}
      {Array.isArray(eta["reasonCodes"]) && (eta["reasonCodes"] as string[]).length > 0 ? (
        <div className="mt-2 flex flex-wrap gap-1">
          {(eta["reasonCodes"] as string[]).map((code) => (
            <span key={code} className="rounded bg-slate-100 px-1.5 py-0.5 text-xs text-slate-600">{code}</span>
          ))}
        </div>
      ) : null}
    </div>
  );
}

// ── SLA risk card ────────────────────────────────────────────────────────────
function SlaRiskCard({ sla }: { sla: AnyRecord }) {
  const riskLevel = String(sla["overallRisk"] ?? "low");
  const borderCls = riskLevel === "high" ? "border-red-300 bg-red-50" : riskLevel === "medium" ? "border-amber-300 bg-amber-50" : "border-teal-200 bg-teal-50";
  const flags = [
    { key: "latePickupRisk",      label: "Late Pickup Risk" },
    { key: "lateDeliveryRisk",    label: "Late Delivery Risk" },
    { key: "maintenanceHoldRisk", label: "Maintenance Hold" },
    { key: "exceptionRisk",       label: "Active Exception" },
  ];
  return (
    <div className={`rounded-2xl border p-4 ${borderCls}`}>
      <p className="text-xs font-bold uppercase tracking-widest text-slate-500">SLA Risk</p>
      <p className={`mt-1 text-sm font-bold capitalize ${riskLevel === "high" ? "text-red-700" : riskLevel === "medium" ? "text-amber-700" : "text-teal-700"}`}>
        {riskLevel} Risk
      </p>
      <ul className="mt-2 space-y-1">
        {flags.filter(f => sla[f.key]).map(f => (
          <li key={f.key} className="flex items-center gap-1.5 text-xs text-red-700">
            <AlertTriangle className="h-3 w-3" /> {f.label}
          </li>
        ))}
        {flags.every(f => !sla[f.key]) ? (
          <li className="flex items-center gap-1.5 text-xs text-teal-700"><ShieldCheck className="h-3 w-3" /> No SLA risks detected</li>
        ) : null}
      </ul>
    </div>
  );
}

// ── Main page ────────────────────────────────────────────────────────────────
export function CustomerVisibilityPage() {
  const [tab, setTab] = useState<Tab>("Dashboard");
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [shareModal, setShareModal] = useState<number | null>(null);
  const [shareResult, setShareResult] = useState<{ token: string; expiresAt: string } | null>(null);
  const [expiryDays, setExpiryDays] = useState(30);
  const [feedbackRating, setFeedbackRating] = useState(5);
  const [feedbackComments, setFeedbackComments] = useState("");
  const [feedbackSentiment, setFeedbackSentiment] = useState<"Positive" | "Neutral" | "Negative">("Positive");
  const [feedbackMessage, setFeedbackMessage] = useState<string | null>(null);

  const hasPermission = useHasPermission();
  const canView    = hasPermission("customer_portal:view");
  const canManage  = hasPermission("customer_portal:manage");
  const qc = useQueryClient();

  const shipments = useQuery<AnyRecord[]>({
    queryKey: ["customer-visibility", "shipments"],
    queryFn: customerVisibilityApi.shipments,
    enabled: canView,
  });

  const detail = useQuery<AnyRecord>({
    queryKey: ["customer-visibility", "shipment", selectedId],
    queryFn: () => customerVisibilityApi.shipmentDetail(selectedId!),
    enabled: canView && selectedId != null,
  });

  const insights = useQuery<AnyRecord>({
    queryKey: ["customer-visibility", "insights"],
    queryFn: customerVisibilityApi.insights,
    enabled: canView,
  });

  const shareMutation = useMutation({
    mutationFn: () => customerVisibilityApi.shareShipment(shareModal!, expiryDays),
    onSuccess: (data) => {
      setShareResult(data);
      void qc.invalidateQueries({ queryKey: ["customer-visibility"] });
    },
  });

  const revokeMutation = useMutation({
    mutationFn: (id: number) => customerVisibilityApi.revokeShare(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["customer-visibility"] }),
  });

  const feedbackMutation = useMutation({
    mutationFn: (payload: { shipmentId: number; rating: number; sentiment: string; comments: string }) =>
      customerEtaApi.feedback(payload.shipmentId, {
        trackingCode: String((detail.data?.shipment as AnyRecord | undefined)?.["trackingCode"] ?? (detail.data?.shipment as AnyRecord | undefined)?.["tracking_code"] ?? ""),
        rating: payload.rating,
        sentiment: payload.sentiment,
        comments: payload.comments,
      }),
    onSuccess: () => {
      setFeedbackMessage("Feedback submitted to the customer service queue.");
      setFeedbackComments("");
      setFeedbackRating(5);
      setFeedbackSentiment("Positive");
      void qc.invalidateQueries({ queryKey: ["customer-visibility"] });
      window.setTimeout(() => setFeedbackMessage(null), 3500);
    },
  });

  if (!canView) {
    return (
      <div className="flex flex-col items-center justify-center gap-4 p-12 text-slate-500">
        <ShieldCheck className="h-10 w-10" />
        <p className="text-sm font-medium">You do not have permission to view customer visibility.</p>
      </div>
    );
  }

  const rows = shipments.data ?? [];
  const insightList: AnyRecord[] = (insights.data?.["insights"] as AnyRecord[]) ?? [];
  const insightStats: AnyRecord = (insights.data?.["stats"] as AnyRecord) ?? {};

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Customer Visibility"
        title="Shipment Tracking & ETA Risk Engine"
        description="Customer-safe tracking tokens, real-time ETA from dispatch and telemetry, SLA risk, and proof of delivery visibility."
        actions={
          <button
            type="button"
            className="btn-ghost"
            onClick={() => exportCsv("customer-visibility", rows)}
          >
            Export
          </button>
        }
      />

      {/* KPIs */}
      <div className="grid gap-4 md:grid-cols-4">
        <KpiCard label="Total Tracked" value={String(insightStats["totalTracked"] ?? rows.length)} icon={<Package />} status="Active" />
        <KpiCard label="Active Shares" value={String(insightStats["activeShares"] ?? "--")} icon={<Share2 />} status="Active" />
        <KpiCard label="In Exception" value={String(insightStats["exceptionCount"] ?? "--")} icon={<AlertTriangle />} status={Number(insightStats["exceptionCount"] ?? 0) > 0 ? "Review" : "Active"} />
        <KpiCard label="Delivered" value={String(insightStats["deliveredCount"] ?? "--")} icon={<CheckCircle />} status="Active" />
      </div>

      {/* ETA insights */}
      {insightList.length > 0 && (
        <div className="grid gap-3 md:grid-cols-2">
          {insightList.map((ins, i) => (
            <AiInsightCard key={i} insight={{ title: String(ins["code"] ?? "ETA Insight"), body: String(ins["message"]) }} />
          ))}
        </div>
      )}

      {/* Tabs */}
      <div className="flex gap-1 rounded-xl bg-slate-100 p-1 w-fit">
        {TABS.map((t) => (
          <button
            key={t}
            type="button"
            onClick={() => setTab(t)}
            className={`rounded-lg px-4 py-2 text-sm font-medium transition ${tab === t ? "bg-white shadow text-slate-900" : "text-slate-500 hover:text-slate-700"}`}
          >
            {t}
          </button>
        ))}
      </div>

      {/* Dashboard tab */}
      {tab === "Dashboard" && (
        <section className="panel p-0 overflow-hidden">
          {shipments.isLoading && <LoadingState />}
          {shipments.isError && <ErrorState message={(shipments.error as Error)?.message} />}
          {!shipments.isLoading && rows.length === 0 && (
            <EmptyState title="No tracked shipments" subtitle="Use 'Token Management' to create customer-facing tracking links." />
          )}
          {rows.length > 0 && (
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-100 bg-slate-50 text-left text-xs font-semibold uppercase tracking-wide text-slate-500">
                  <th className="px-4 py-3">Shipment</th>
                  <th className="px-4 py-3">Customer</th>
                  <th className="px-4 py-3">Status</th>
                  <th className="px-4 py-3">ETA Risk</th>
                  <th className="px-4 py-3">Confidence</th>
                  <th className="px-4 py-3">Planned Delivery</th>
                  <th className="px-4 py-3">Share</th>
                  <th className="px-4 py-3"></th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rows.map((row) => (
                  <tr key={String(row["id"])} className="hover:bg-slate-50 transition">
                    <td className="px-4 py-3 font-medium text-slate-900">{String(row["shipmentNumber"] ?? "--")}</td>
                    <td className="px-4 py-3 text-slate-600">{String(row["customerName"] ?? "--")}</td>
                    <td className="px-4 py-3"><StatusBadge status={row["assignmentStatus"]} /></td>
                    <td className="px-4 py-3"><EtaRiskBadge risk={row["etaRisk"]} /></td>
                    <td className="px-4 py-3"><ConfidencePill confidence={row["etaConfidence"]} /></td>
                    <td className="px-4 py-3 text-slate-500">
                      {row["plannedDeliveryAt"] != null ? new Date(String(row["plannedDeliveryAt"])).toLocaleDateString() : "--"}
                    </td>
                    <td className="px-4 py-3">
                      {row["shareEnabled"] ? (
                        <span className="flex items-center gap-1 text-xs text-teal-600"><Eye className="h-3 w-3" /> Active</span>
                      ) : (
                        <span className="flex items-center gap-1 text-xs text-slate-400"><EyeOff className="h-3 w-3" /> Off</span>
                      )}
                    </td>
                    <td className="px-4 py-3">
                      <button
                        type="button"
                        className="btn-ghost text-xs"
                        onClick={() => { setSelectedId(Number(row["shipmentId"])); setTab("Shipment Detail"); }}
                      >
                        Detail
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </section>
      )}

      {/* Shipment Detail tab */}
      {tab === "Shipment Detail" && (
        <div className="space-y-4">
          {/* Shipment selector */}
          {rows.length > 0 && (
            <div className="flex items-center gap-3">
              <label className="text-sm font-medium text-slate-700">Select shipment:</label>
              <select
                className="rounded-lg border border-slate-200 px-3 py-2 text-sm"
                value={selectedId ?? ""}
                onChange={(e) => setSelectedId(e.target.value ? Number(e.target.value) : null)}
              >
                <option value="">-- Select --</option>
                {rows.map((r) => (
                  <option key={String(r["shipmentId"])} value={String(r["shipmentId"])}>
                    {String(r["shipmentNumber"] ?? r["shipmentId"])} — {String(r["customerName"] ?? "")}
                  </option>
                ))}
              </select>
            </div>
          )}

          {detail.isLoading && <LoadingState />}
          {detail.isError && <ErrorState message={(detail.error as Error)?.message} />}
          {detail.data && (() => {
            const d = detail.data as AnyRecord;
            const shipment = d["shipment"] as AnyRecord ?? {};
            const eta = d["eta"] as AnyRecord ?? {};
            const sla = d["slaRisk"] as AnyRecord ?? {};
            const timeline = (d["timeline"] as AnyRecord[]) ?? [];
            const shipmentId = Number(shipment["shipmentId"] ?? selectedId ?? 0);
            const trackingCode = String(shipment["trackingCode"] ?? shipment["tracking_code"] ?? "");

            return (
              <div className="grid gap-4 lg:grid-cols-[1fr_320px]">
                <div className="space-y-4">
                  {/* Shipment header */}
                  <div className="panel p-5">
                    <div className="flex items-start justify-between">
                      <div>
                        <p className="text-xs font-bold uppercase tracking-widest text-slate-400">Shipment</p>
                        <p className="mt-1 text-xl font-bold text-slate-900">{String(shipment["shipmentNumber"] ?? "--")}</p>
                        <p className="text-sm text-slate-500">{String(shipment["customerName"] ?? "--")}</p>
                      </div>
                      <StatusBadge status={shipment["currentStatus"]} />
                    </div>
                    <div className="mt-4 grid gap-2 text-sm md:grid-cols-2">
                      <div className="flex items-center gap-2 text-slate-600">
                        <MapPin className="h-4 w-4 text-slate-400" />
                        <span>Pickup: {String(shipment["pickupAddress"] ?? "--")}</span>
                      </div>
                      <div className="flex items-center gap-2 text-slate-600">
                        <Truck className="h-4 w-4 text-slate-400" />
                        <span>Delivery: {String(shipment["dropoffAddress"] ?? "--")}</span>
                      </div>
                    </div>
                  </div>

                  {/* Timeline */}
                  <div className="panel p-5">
                    <p className="text-xs font-bold uppercase tracking-widest text-slate-400 mb-4">Event Timeline</p>
                    {timeline.length === 0 ? (
                      <p className="text-sm text-slate-400">No events recorded yet.</p>
                    ) : (
                      <div className="space-y-0">
                        {timeline.map((evt, i) => (
                          <TimelineItem key={i} event={evt as AnyRecord} last={i === timeline.length - 1} />
                        ))}
                      </div>
                    )}
                  </div>
                </div>

                <div className="space-y-4">
                  <EtaCard eta={eta} />
                  <SlaRiskCard sla={sla} />

                  {/* Proof section */}
                  <div className="panel p-4">
                    <p className="text-xs font-bold uppercase tracking-widest text-slate-400 mb-3">Proof of Delivery</p>
                    {timeline.filter(t => (t as AnyRecord)["category"] === "proof").length === 0 ? (
                      <p className="text-sm text-slate-400">No proof recorded yet.</p>
                    ) : (
                      timeline.filter(t => (t as AnyRecord)["category"] === "proof").map((p, i) => (
                        <div key={i} className="flex items-center gap-2 text-sm text-teal-700">
                          <CheckCircle className="h-4 w-4" />
                          <span>{String((p as AnyRecord)["eventType"])} — {(p as AnyRecord)["occurredAt"] != null ? new Date(String((p as AnyRecord)["occurredAt"])).toLocaleString() : "--"}</span>
                        </div>
                      ))
                    )}
                  </div>

                  <div className="panel p-4">
                    <p className="text-xs font-bold uppercase tracking-widest text-slate-400 mb-3">Customer Feedback & Complaint Intake</p>
                    <div className="space-y-3">
                      <div className="grid grid-cols-2 gap-3">
                        <label className="space-y-1">
                          <span className="text-[11px] font-semibold uppercase tracking-wide text-slate-500">Rating</span>
                          <select
                            className="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm"
                            value={feedbackRating}
                            onChange={(e) => {
                              const next = Number(e.target.value);
                              setFeedbackRating(next);
                              setFeedbackSentiment(next >= 4 ? "Positive" : next >= 3 ? "Neutral" : "Negative");
                            }}
                          >
                            {[5, 4, 3, 2, 1].map((value) => (
                              <option key={value} value={value}>{value} Stars</option>
                            ))}
                          </select>
                        </label>
                        <label className="space-y-1">
                          <span className="text-[11px] font-semibold uppercase tracking-wide text-slate-500">Sentiment</span>
                          <select
                            className="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm"
                            value={feedbackSentiment}
                            onChange={(e) => setFeedbackSentiment(e.target.value as typeof feedbackSentiment)}
                          >
                            {["Positive", "Neutral", "Negative"].map((value) => (
                              <option key={value} value={value}>{value}</option>
                            ))}
                          </select>
                        </label>
                      </div>
                      <label className="space-y-1 block">
                        <span className="text-[11px] font-semibold uppercase tracking-wide text-slate-500">Comments</span>
                        <textarea
                          className="min-h-24 w-full rounded-lg border border-slate-200 px-3 py-2 text-sm"
                          placeholder="Tell us what worked well or what needs attention..."
                          value={feedbackComments}
                          onChange={(e) => setFeedbackComments(e.target.value)}
                        />
                      </label>
                      {feedbackMessage ? (
                        <p className="text-xs font-medium text-teal-700">{feedbackMessage}</p>
                      ) : null}
                      <div className="flex flex-wrap items-center gap-2">
                        <button
                          type="button"
                          className="btn-primary text-xs"
                          disabled={shipmentId <= 0 || feedbackMutation.isPending}
                          onClick={() => feedbackMutation.mutate({
                            shipmentId,
                            rating: feedbackRating,
                            sentiment: feedbackSentiment,
                            comments: feedbackComments,
                          })}
                        >
                          {feedbackMutation.isPending ? (
                            "Sending…"
                          ) : (
                            <>
                              <Send className="h-3.5 w-3.5" />
                              Submit Feedback
                            </>
                          )}
                        </button>
                        <span className="text-xs text-slate-500">
                          Uses customer-safe shipment data only{trackingCode ? ` for ${trackingCode}` : ""}.
                        </span>
                      </div>
                    </div>
                  </div>
                </div>
              </div>
            );
          })()}
          {!detail.data && !detail.isLoading && selectedId == null && (
            <EmptyState title="Select a shipment" subtitle="Choose a shipment from the dropdown above to view ETA, timeline, and proof." />
          )}
        </div>
      )}

      {/* Token Management tab */}
      {tab === "Token Management" && (
        <div className="space-y-4">
          {!canManage ? (
            <div className="rounded-2xl border border-amber-200 bg-amber-50 p-4 text-sm text-amber-800">
              You need <code>customer_portal:manage</code> permission to create or revoke tracking links.
            </div>
          ) : null}

          <section className="panel p-0 overflow-hidden">
            {rows.length === 0 ? (
              <EmptyState title="No tracked shipments" subtitle="No shipments have been shared yet." />
            ) : (
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-slate-100 bg-slate-50 text-left text-xs font-semibold uppercase tracking-wide text-slate-500">
                    <th className="px-4 py-3">Shipment</th>
                    <th className="px-4 py-3">Customer</th>
                    <th className="px-4 py-3">Share Status</th>
                    <th className="px-4 py-3">Expires</th>
                    <th className="px-4 py-3">Actions</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {rows.map((row) => (
                    <tr key={String(row["id"])} className="hover:bg-slate-50">
                      <td className="px-4 py-3 font-medium text-slate-900">{String(row["shipmentNumber"] ?? "--")}</td>
                      <td className="px-4 py-3 text-slate-600">{String(row["customerName"] ?? "--")}</td>
                      <td className="px-4 py-3">
                        {row["shareEnabled"] && row["visibilityStatus"] === "active" ? (
                          <span className="inline-flex items-center gap-1 text-xs font-semibold text-teal-700 bg-teal-50 px-2 py-0.5 rounded-full">
                            <Eye className="h-3 w-3" /> Active
                          </span>
                        ) : (
                          <span className="inline-flex items-center gap-1 text-xs font-semibold text-slate-500 bg-slate-100 px-2 py-0.5 rounded-full">
                            <EyeOff className="h-3 w-3" /> Inactive
                          </span>
                        )}
                      </td>
                      <td className="px-4 py-3 text-slate-500 text-xs">
                        {row["expiresAt"] != null ? new Date(String(row["expiresAt"])).toLocaleDateString() : "--"}
                      </td>
                      <td className="px-4 py-3">
                        <div className="flex gap-2">
                          {canManage && (
                            <>
                              <button
                                type="button"
                                className="btn-ghost text-xs"
                                onClick={() => { setShareModal(Number(row["shipmentId"])); setShareResult(null); }}
                              >
                                <Share2 className="h-3 w-3" /> New Link
                              </button>
                              {row["shareEnabled"] ? (
                                <button
                                  type="button"
                                  className="btn-ghost text-xs text-red-600"
                                  onClick={() => revokeMutation.mutate(Number(row["shipmentId"]))}
                                >
                                  <EyeOff className="h-3 w-3" /> Revoke
                                </button>
                              ) : null}
                            </>
                          )}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </section>
        </div>
      )}

      {tab === "Feedback" && (
        <div className="grid gap-4 lg:grid-cols-[1fr_340px]">
          <section className="panel p-5">
            <div className="flex items-start justify-between gap-3">
              <div>
                <p className="text-xs font-bold uppercase tracking-widest text-slate-400">Feedback queue</p>
                <h2 className="mt-1 text-lg font-bold text-slate-900">Complaint and experience intake</h2>
                <p className="mt-1 text-sm text-slate-500">
                  Submit customer feedback from a selected shipment without exposing internal cost, risk, or operational notes.
                </p>
              </div>
              <MessageSquare className="h-5 w-5 text-teal-500" />
            </div>
            <div className="mt-4 grid gap-3 md:grid-cols-3">
              <div className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
                <p className="text-[11px] font-bold uppercase tracking-[0.14em] text-slate-400">Selected shipment</p>
                <p className="mt-1 text-sm font-semibold text-slate-900">{selectedId != null ? `Shipment ${selectedId}` : "No shipment selected"}</p>
              </div>
              <div className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
                <p className="text-[11px] font-bold uppercase tracking-[0.14em] text-slate-400">Rating</p>
                <p className="mt-1 text-sm font-semibold text-slate-900">{feedbackRating} / 5</p>
              </div>
              <div className="rounded-2xl border border-slate-200 bg-slate-50 p-4">
                <p className="text-[11px] font-bold uppercase tracking-[0.14em] text-slate-400">Sentiment</p>
                <p className="mt-1 text-sm font-semibold text-slate-900">{feedbackSentiment}</p>
              </div>
            </div>
            <div className="mt-4 grid gap-3 md:grid-cols-[1fr_1fr]">
              <label className="space-y-1">
                <span className="text-[11px] font-semibold uppercase tracking-wide text-slate-500">Shipment</span>
                <select
                  className="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm"
                  value={selectedId ?? ""}
                  onChange={(e) => setSelectedId(e.target.value ? Number(e.target.value) : null)}
                >
                  <option value="">Select a shipment</option>
                  {rows.map((row) => (
                    <option key={String(row["shipmentId"])} value={String(row["shipmentId"])}>
                      {String(row["shipmentNumber"] ?? row["shipmentId"])} - {String(row["customerName"] ?? "")}
                    </option>
                  ))}
                </select>
              </label>
              <label className="space-y-1">
                <span className="text-[11px] font-semibold uppercase tracking-wide text-slate-500">Priority</span>
                <select
                  className="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm"
                  value={feedbackSentiment}
                  onChange={(e) => setFeedbackSentiment(e.target.value as typeof feedbackSentiment)}
                >
                  <option value="Positive">Positive</option>
                  <option value="Neutral">Neutral</option>
                  <option value="Negative">Negative</option>
                </select>
              </label>
            </div>
            <label className="mt-4 block space-y-1">
              <span className="text-[11px] font-semibold uppercase tracking-wide text-slate-500">Comment</span>
              <textarea
                className="min-h-32 w-full rounded-lg border border-slate-200 px-3 py-2 text-sm"
                placeholder="Describe the customer experience, complaint, or improvement request."
                value={feedbackComments}
                onChange={(e) => setFeedbackComments(e.target.value)}
              />
            </label>
            {feedbackMessage ? <p className="mt-3 text-sm font-medium text-teal-700">{feedbackMessage}</p> : null}
            <div className="mt-4 flex flex-wrap items-center gap-2">
              <button
                type="button"
                className="btn-primary text-sm"
                disabled={selectedId == null || feedbackMutation.isPending}
                onClick={() => feedbackMutation.mutate({
                  shipmentId: Number(selectedId),
                  rating: feedbackRating,
                  sentiment: feedbackSentiment,
                  comments: feedbackComments,
                })}
              >
                {feedbackMutation.isPending ? "Sending…" : "Submit to Service Queue"}
              </button>
              <span className="text-xs text-slate-500">
                Feedback will follow the same customer-safe intake path used by ETA tracking.
              </span>
            </div>
          </section>

          <aside className="panel p-5">
            <p className="text-xs font-bold uppercase tracking-widest text-slate-400">What this protects</p>
            <ul className="mt-3 space-y-2 text-sm text-slate-600">
              <li className="flex items-start gap-2"><Star className="mt-0.5 h-4 w-4 text-teal-500" />No internal margin, cost, or driver scoring is exposed.</li>
              <li className="flex items-start gap-2"><ShieldCheck className="mt-0.5 h-4 w-4 text-teal-500" />Cross-customer data stays hidden by the session boundary.</li>
              <li className="flex items-start gap-2"><CheckCircle className="mt-0.5 h-4 w-4 text-teal-500" />Complaints use the same durable feedback path as live ETA updates.</li>
            </ul>
          </aside>
        </div>
      )}

      {/* Share Modal */}
      {shareModal != null && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
          <div className="bg-white rounded-2xl shadow-xl p-6 w-full max-w-md">
            <p className="text-base font-bold text-slate-900 mb-4">Create Customer Tracking Link</p>
            {shareResult ? (
              <div className="space-y-3">
                <div className="rounded-lg bg-teal-50 border border-teal-200 p-3">
                  <p className="text-xs font-bold text-teal-700 mb-1">Tracking token created</p>
                  <code className="block break-all text-xs text-teal-900 font-mono">{shareResult.token}</code>
                  <p className="text-xs text-teal-600 mt-1">Expires: {new Date(shareResult.expiresAt).toLocaleDateString()}</p>
                </div>
                <p className="text-xs text-slate-500 flex items-center gap-1">
                  <ExternalLink className="h-3 w-3" />
                  Share the tracking token with the customer to allow them to view live shipment status.
                </p>
                <div className="flex gap-2">
                  <button
                    type="button"
                    className="btn-primary flex-1"
                    onClick={() => { void navigator.clipboard.writeText(shareResult.token); }}
                  >
                    Copy Token
                  </button>
                  <button type="button" className="btn-ghost" onClick={() => setShareModal(null)}>Close</button>
                </div>
              </div>
            ) : (
              <div className="space-y-4">
                <div>
                  <label className="text-xs font-medium text-slate-600 block mb-1">Expiry (days)</label>
                  <select
                    className="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm"
                    value={expiryDays}
                    onChange={(e) => setExpiryDays(Number(e.target.value))}
                  >
                    <option value={7}>7 days</option>
                    <option value={14}>14 days</option>
                    <option value={30}>30 days (default)</option>
                    <option value={60}>60 days</option>
                    <option value={90}>90 days</option>
                  </select>
                </div>
                <p className="text-xs text-slate-500">
                  A unique, expiring, revocable tracking token will be generated for this shipment. The customer sees shipment status, ETA, timeline, and proof — no internal operational data is exposed.
                </p>
                <div className="flex gap-2">
                  <button
                    type="button"
                    className="btn-primary flex-1"
                    disabled={shareMutation.isPending}
                    onClick={() => shareMutation.mutate()}
                  >
                    {shareMutation.isPending ? "Generating…" : "Generate Link"}
                  </button>
                  <button type="button" className="btn-ghost" onClick={() => setShareModal(null)}>Cancel</button>
                </div>
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
