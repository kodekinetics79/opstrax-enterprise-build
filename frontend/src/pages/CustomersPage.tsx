import {
  useState,
  useEffect,
  useMemo,
  useRef,
  useCallback,
  type ReactNode,
} from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import {
  Plus,
  Download,
  Pencil,
  Trash2,
  RotateCcw,
  Users,
  ShieldCheck,
  AlertTriangle,
  Gauge,
  Crown,
  Truck,
  FileText,
  MapPin,
  Sparkles,
  Clock,
  X,
  Search,
  ExternalLink,
  Tag,
  Activity,
  MailWarning,
} from "lucide-react";
import {
  customersApi,
  type BulkAction,
  type BulkResponse,
  type CustomerStatus,
  type CustomerSlaTier,
  type CustomerTimelineEntry,
  type CustomerRecommendation,
} from "@/services/customersApi";
import { useRowSelection, BulkCheckbox, BulkBar, ConfirmDialog } from "@/components/bulk";
import {
  ClayCard,
  ClayButton,
  ClayStat,
  ClayBadge,
  ClayGauge,
  ClayInput,
  ClaySelect,
  ClayWell,
  ClaySkeleton,
  ClayStatSkeleton,
  type ClayTone,
} from "@/components/clay";
import { useHasPermission, PERMISSIONS } from "@/hooks/usePermission";
import { exportCsv, LoadingState, ErrorState, EmptyState } from "@/components/ui";
import type { AnyRecord } from "@/types";

// ── Domain vocabulary ─────────────────────────────────────────────────────────
// The canonical status/tier vocabularies the bulk contract accepts. Kept here so
// the filter chips, the row badges, and the bulk mutation all speak one language.
const STATUSES: CustomerStatus[] = ["Active", "At Risk", "Inactive"];
const TIERS: CustomerSlaTier[] = ["Standard", "Gold", "Platinum"];

const statusTone = (status: unknown): ClayTone => {
  switch (String(status)) {
    case "Active": return "good";
    case "At Risk": return "bad";
    case "Inactive": return "neutral";
    default: return "warn";
  }
};

const tierTone = (tier: unknown): ClayTone => {
  switch (String(tier)) {
    case "Platinum": return "info";
    case "Gold": return "warn";
    default: return "neutral";
  }
};

const riskTone = (risk: unknown): ClayTone => {
  switch (String(risk)) {
    case "High": return "bad";
    case "Medium": return "warn";
    case "Low": return "good";
    default: return "neutral";
  }
};

/** null / undefined -> real score number or null. Never coerces null to 0 — a
 * customer with no delivery history must read as "no data", not as a zero score. */
const toScore = (v: unknown): number | null =>
  v === null || v === undefined || v === "" ? null : Number(v);

const fmtInt = (v: unknown): string =>
  v === null || v === undefined || v === "" ? "—" : String(v);

// ── Focus-trapping modal (Escape closes, Tab wraps) ─────────────────────────────
// Used for the create / edit / set-status / set-tier forms. Traps keyboard focus,
// restores it to the trigger on unmount, and closes on Escape.
function Modal({
  title,
  onClose,
  children,
  footer,
  wide = false,
}: {
  title: string;
  onClose: () => void;
  children: ReactNode;
  footer?: ReactNode;
  wide?: boolean;
}) {
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const prev = document.activeElement as HTMLElement | null;
    const node = ref.current;
    const focusables = () =>
      node
        ? Array.from(
            node.querySelectorAll<HTMLElement>(
              'button,[href],input,select,textarea,[tabindex]:not([tabindex="-1"])',
            ),
          ).filter((el) => !el.hasAttribute("disabled"))
        : [];
    focusables()[0]?.focus();

    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.stopPropagation();
        onClose();
        return;
      }
      if (e.key === "Tab") {
        const items = focusables();
        if (items.length === 0) return;
        const first = items[0];
        const last = items[items.length - 1];
        if (e.shiftKey && document.activeElement === first) {
          e.preventDefault();
          last.focus();
        } else if (!e.shiftKey && document.activeElement === last) {
          e.preventDefault();
          first.focus();
        }
      }
    };
    document.addEventListener("keydown", onKey, true);
    return () => {
      document.removeEventListener("keydown", onKey, true);
      prev?.focus?.();
    };
  }, [onClose]);

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/50 p-4 backdrop-blur-sm"
      onClick={onClose}
    >
      <div
        ref={ref}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        className={wide ? "w-full max-w-2xl" : "w-full max-w-lg"}
        onClick={(e) => e.stopPropagation()}
      >
        <ClayCard
          title={title}
          actions={
            <ClayButton size="sm" icon={X} aria-label="Close dialog" onClick={onClose} />
          }
          footer={footer}
        >
          {children}
        </ClayCard>
      </div>
    </div>
  );
}

// ── Customer form (create + edit) ───────────────────────────────────────────────
type FormState = {
  name: string;
  customerCode: string;
  contactName: string;
  email: string;
  phone: string;
  billingAddress: string;
  shippingAddress: string;
  status: CustomerStatus;
  slaTier: CustomerSlaTier;
};

const emptyForm = (): FormState => ({
  name: "",
  customerCode: "",
  contactName: "",
  email: "",
  phone: "",
  billingAddress: "",
  shippingAddress: "",
  status: "Active",
  slaTier: "Standard",
});

const formFromRecord = (r: AnyRecord): FormState => ({
  name: String(r.name ?? ""),
  customerCode: String(r.customerCode ?? ""),
  contactName: String(r.contactName ?? ""),
  email: String(r.email ?? ""),
  phone: String(r.phone ?? ""),
  billingAddress: String(r.billingAddress ?? ""),
  shippingAddress: String(r.shippingAddress ?? ""),
  status: (STATUSES.includes(r.status as CustomerStatus) ? r.status : "Active") as CustomerStatus,
  slaTier: (TIERS.includes(r.slaTier as CustomerSlaTier) ? r.slaTier : "Standard") as CustomerSlaTier,
});

function CustomerForm({
  mode,
  initial,
  onClose,
  onSaved,
}: {
  mode: "create" | "edit";
  initial?: AnyRecord;
  onClose: () => void;
  onSaved: () => void;
}) {
  const qc = useQueryClient();
  const [form, setForm] = useState<FormState>(initial ? formFromRecord(initial) : emptyForm());
  const set = (k: keyof FormState) => (v: string) => setForm((f) => ({ ...f, [k]: v }));

  const mut = useMutation({
    mutationFn: () =>
      mode === "create"
        ? customersApi.create(form as unknown as AnyRecord)
        : customersApi.update(initial!.id as string | number, form as unknown as AnyRecord),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ["customers"] });
      onSaved();
    },
  });

  const valid = form.name.trim() && form.customerCode.trim();

  return (
    <Modal
      title={mode === "create" ? "New customer account" : `Edit — ${initial?.name ?? ""}`}
      onClose={onClose}
      wide
      footer={
        <div className="flex items-center justify-between gap-3">
          <p className="text-[0.72rem] font-medium text-red-700" role="alert">
            {mut.isError ? (mut.error as Error)?.message ?? "Save failed." : ""}
          </p>
          <div className="flex gap-2">
            <ClayButton variant="ghost" size="sm" onClick={onClose}>
              Cancel
            </ClayButton>
            <ClayButton
              variant="primary"
              size="sm"
              icon={mode === "create" ? Plus : Pencil}
              loading={mut.isPending}
              disabled={!valid}
              onClick={() => mut.mutate()}
            >
              {mode === "create" ? "Create customer" : "Save changes"}
            </ClayButton>
          </div>
        </div>
      }
    >
      <div className="grid grid-cols-1 gap-3.5 sm:grid-cols-2">
        <ClayInput
          label="Company name"
          wrapperClassName="sm:col-span-2"
          placeholder="Gulf Express Logistics"
          value={form.name}
          onChange={(e) => set("name")(e.target.value)}
        />
        <ClayInput
          label="Customer code"
          placeholder="CUS-001"
          value={form.customerCode}
          onChange={(e) => set("customerCode")(e.target.value)}
        />
        <ClayInput
          label="Primary contact"
          placeholder="Jane Doe"
          value={form.contactName}
          onChange={(e) => set("contactName")(e.target.value)}
        />
        <ClayInput
          label="Email"
          type="email"
          placeholder="ops@company.com"
          value={form.email}
          onChange={(e) => set("email")(e.target.value)}
        />
        <ClayInput
          label="Phone"
          placeholder="+966 11 000 0000"
          value={form.phone}
          onChange={(e) => set("phone")(e.target.value)}
        />
        <ClaySelect
          label="Account status"
          value={form.status}
          onChange={(e) => set("status")(e.target.value)}
        >
          {STATUSES.map((s) => (
            <option key={s} value={s}>{s}</option>
          ))}
        </ClaySelect>
        <ClaySelect
          label="SLA tier"
          value={form.slaTier}
          onChange={(e) => set("slaTier")(e.target.value)}
        >
          {TIERS.map((t) => (
            <option key={t} value={t}>{t}</option>
          ))}
        </ClaySelect>
        <ClayInput
          label="Billing address"
          wrapperClassName="sm:col-span-2"
          placeholder="Street, city, region"
          value={form.billingAddress}
          onChange={(e) => set("billingAddress")(e.target.value)}
        />
        <ClayInput
          label="Shipping address"
          wrapperClassName="sm:col-span-2"
          placeholder="Street, city, region"
          value={form.shippingAddress}
          onChange={(e) => set("shippingAddress")(e.target.value)}
        />
      </div>
      <p className="mt-3 text-[0.72rem] font-medium leading-snug text-slate-500">
        New accounts start with no SLA health or delivery score — those are computed from real
        delivery history once jobs complete, never seeded.
      </p>
    </Modal>
  );
}

// ── Bulk "set value" modal (status / tier) ──────────────────────────────────────
function SetValueModal({
  kind,
  count,
  busy,
  onApply,
  onClose,
}: {
  kind: "status" | "tier";
  count: number;
  busy: boolean;
  onApply: (value: string) => void;
  onClose: () => void;
}) {
  const options = kind === "status" ? STATUSES : TIERS;
  const [value, setValue] = useState<string>(options[0]);
  return (
    <Modal
      title={kind === "status" ? "Set account status" : "Set SLA tier"}
      onClose={onClose}
      footer={
        <div className="flex justify-end gap-2">
          <ClayButton variant="ghost" size="sm" onClick={onClose}>
            Cancel
          </ClayButton>
          <ClayButton
            variant="primary"
            size="sm"
            icon={kind === "status" ? Activity : Tag}
            loading={busy}
            onClick={() => onApply(value)}
          >
            Apply to {count}
          </ClayButton>
        </div>
      }
    >
      <p className="mb-3 text-sm font-medium text-slate-600">
        Update {count} selected {count === 1 ? "customer" : "customers"} to a new{" "}
        {kind === "status" ? "account status" : "SLA tier"}.
      </p>
      <ClaySelect
        label={kind === "status" ? "New status" : "New tier"}
        value={value}
        onChange={(e) => setValue(e.target.value)}
      >
        {options.map((o) => (
          <option key={o} value={o}>{o}</option>
        ))}
      </ClaySelect>
    </Modal>
  );
}

// ── Score cell — honest about missing data ──────────────────────────────────────
function ScoreCell({ score }: { score: number | null }) {
  if (score === null || !Number.isFinite(score)) {
    return <span className="text-[0.72rem] font-semibold text-slate-600">Unrated</span>;
  }
  const pct = Math.min(100, Math.max(0, score));
  const tone: ClayTone = pct >= 85 ? "good" : pct >= 70 ? "info" : pct >= 50 ? "warn" : "bad";
  const color =
    tone === "good" ? "#059669" : tone === "info" ? "#2563eb" : tone === "warn" ? "#d97706" : "#dc2626";
  return (
    <div className="flex items-center gap-2">
      <div className="cx-well h-1.5 w-16 overflow-hidden rounded-full">
        <div className="h-full rounded-full" style={{ width: `${pct}%`, background: color }} />
      </div>
      <span className="w-7 text-right text-[0.78rem] font-bold tabular-nums text-slate-800">
        {Math.round(pct)}
      </span>
    </div>
  );
}

// ── Related-record row inside the detail panel ──────────────────────────────────
function RelatedRow({
  to,
  primary,
  secondary,
  badge,
}: {
  to?: string;
  primary: string;
  secondary?: string;
  badge?: ReactNode;
}) {
  const inner = (
    <div className="flex items-center gap-2.5 rounded-[12px] px-3 py-2 transition-colors hover:bg-white/60">
      <div className="min-w-0 flex-1">
        <p className="truncate text-[0.8rem] font-semibold text-slate-800">{primary}</p>
        {secondary && <p className="truncate text-[0.72rem] font-medium text-slate-500">{secondary}</p>}
      </div>
      {badge}
      {to && <ExternalLink size={13} strokeWidth={2.3} className="shrink-0 text-slate-400" aria-hidden />}
    </div>
  );
  return to ? (
    <Link to={to} className="block focus:outline-none focus-visible:ring-2 focus-visible:ring-teal-400/40">
      {inner}
    </Link>
  ) : (
    inner
  );
}

function RelSection({
  icon: Icon,
  title,
  count,
  children,
  empty,
}: {
  icon: typeof Truck;
  title: string;
  count: number;
  children: ReactNode;
  empty: string;
}) {
  return (
    <section className="flex flex-col gap-1">
      <div className="flex items-center gap-2 px-1">
        <Icon size={14} strokeWidth={2.3} className="text-teal-700" aria-hidden />
        <h4 className="text-[0.72rem] font-bold uppercase tracking-[0.1em] text-slate-700">{title}</h4>
        <ClayBadge tone="neutral" className="ml-auto !px-2 !py-0.5 !text-[0.66rem]">
          {count}
        </ClayBadge>
      </div>
      {count === 0 ? (
        <p className="px-3 py-1.5 text-[0.74rem] font-medium text-slate-600">{empty}</p>
      ) : (
        <div className="flex flex-col">{children}</div>
      )}
    </section>
  );
}

// ── Detail panel ────────────────────────────────────────────────────────────────
function DetailPanel({
  customer,
  onClose,
  onEdit,
  onDelete,
  canUpdate,
  canDelete,
}: {
  customer: AnyRecord;
  onClose: () => void;
  onEdit: () => void;
  onDelete: () => void;
  canUpdate: boolean;
  canDelete: boolean;
}) {
  const id = customer.id as string | number;
  const detailQ = useQuery({ queryKey: ["customers", "detail", id], queryFn: () => customersApi.detail(id) });
  const timelineQ = useQuery({
    queryKey: ["customers", "timeline", id],
    queryFn: () => customersApi.timeline(id),
  });
  const recsQ = useQuery({
    queryKey: ["customers", "recommendations", id],
    queryFn: () => customersApi.recommendations(id),
  });

  const detail = (detailQ.data ?? {}) as AnyRecord;
  const record = (detail.record as AnyRecord) ?? customer;
  const jobs = (detail.activeJobs as AnyRecord[] | undefined) ?? [];
  const contracts = (detail.contracts as AnyRecord[] | undefined) ?? [];
  const sites = (detail.sites as AnyRecord[] | undefined) ?? [];
  const comms = (detail.communications as AnyRecord[] | undefined) ?? [];
  const timeline = (timelineQ.data ?? []) as CustomerTimelineEntry[];
  const recs = (recsQ.data ?? []) as CustomerRecommendation[];

  const sla = toScore(record.slaHealthScore ?? customer.slaHealthScore);
  const dx = toScore(
    record.deliveryExperienceScore ??
      customer.deliveryExperienceScore ??
      customer.customerDeliveryExperienceScore,
  );

  return (
    <ClayCard
      fill
      scrollBody
      className="h-full"
      title={String(record.name ?? "Customer")}
      subtitle={`${String(record.customerCode ?? "—")} · ${String(record.contactName ?? "No contact")}`}
      icon={Users}
      actions={<ClayButton size="sm" icon={X} aria-label="Close detail panel" onClick={onClose} />}
      bodyClassName="flex flex-col gap-5"
      footer={
        <div className="flex items-center justify-end gap-2">
          {canUpdate && (
            <ClayButton size="sm" variant="ghost" icon={Pencil} onClick={onEdit}>
              Edit
            </ClayButton>
          )}
          {canDelete && (
            <ClayButton size="sm" variant="danger" icon={Trash2} onClick={onDelete}>
              Delete
            </ClayButton>
          )}
        </div>
      }
    >
      {/* Status chips */}
      <div className="flex flex-wrap gap-2">
        <ClayBadge tone={statusTone(record.status)} dot>
          {String(record.status ?? "Active")}
        </ClayBadge>
        <ClayBadge tone={tierTone(record.slaTier)} icon={Crown}>
          {String(record.slaTier ?? "Standard")}
        </ClayBadge>
        <ClayBadge tone={riskTone(record.riskHeatScore)} icon={AlertTriangle}>
          {String(record.riskHeatScore ?? "Unrated")} risk
        </ClayBadge>
      </div>

      {/* Health gauges — render "not enough data" honestly */}
      <div className="grid grid-cols-2 gap-3">
        <ClayWell padded className="flex items-center justify-center py-4">
          <ClayGauge
            score={sla}
            size={128}
            label="SLA Health"
            emptyHint="No completed deliveries yet to score SLA health."
          />
        </ClayWell>
        <ClayWell padded className="flex items-center justify-center py-4">
          <ClayGauge
            score={dx}
            size={128}
            label="Delivery Exp."
            emptyHint="Delivery experience appears once jobs complete."
          />
        </ClayWell>
      </div>

      {/* Contact facts */}
      <div className="grid grid-cols-2 gap-2.5">
        {[
          ["Email", record.email],
          ["Phone", record.phone],
          ["Billing", record.billingAddress],
          ["Shipping", record.shippingAddress],
        ].map(([k, v]) => (
          <div key={String(k)} className="min-w-0">
            <p className="text-[0.66rem] font-bold uppercase tracking-[0.1em] text-slate-500">{String(k)}</p>
            <p className="mt-0.5 truncate text-[0.8rem] font-semibold text-slate-800">
              {v ? String(v) : "—"}
            </p>
          </div>
        ))}
      </div>

      {/* Entity relationships — this account wired into the rest of the product */}
      <div className="flex flex-col gap-4">
        <RelSection icon={Truck} title="Active jobs" count={jobs.length} empty="No open jobs for this account.">
          {jobs.slice(0, 8).map((j) => (
            <RelatedRow
              key={String(j.id)}
              to="/jobs"
              primary={String(j.jobNumber ?? j.jobCode ?? `Job #${j.id}`)}
              secondary={`${String(j.pickupAddress ?? "—")} → ${String(j.dropoffAddress ?? "—")}`}
              badge={<ClayBadge tone={riskTone(j.riskHeatScore)} className="!px-2 !py-0.5 !text-[0.64rem]">{String(j.status ?? "")}</ClayBadge>}
            />
          ))}
        </RelSection>

        <RelSection icon={FileText} title="Contracts" count={contracts.length} empty="No contracts on file.">
          {contracts.slice(0, 6).map((ct) => (
            <RelatedRow
              key={String(ct.id)}
              to="/contracts"
              primary={String(ct.contractNumber ?? ct.contractType ?? `Contract #${ct.id}`)}
              secondary={ct.expirationDate ? `Expires ${String(ct.expirationDate).slice(0, 10)}` : undefined}
              badge={<ClayBadge tone={statusTone(ct.status)} className="!px-2 !py-0.5 !text-[0.64rem]">{String(ct.status ?? "")}</ClayBadge>}
            />
          ))}
        </RelSection>

        <RelSection icon={MapPin} title="Operational sites" count={sites.length} empty="No sites captured yet.">
          {sites.slice(0, 6).map((st) => (
            <RelatedRow
              key={String(st.id)}
              primary={String(st.siteName ?? st.siteCode ?? "Site")}
              secondary={[st.city, st.state].filter(Boolean).map(String).join(", ") || String(st.siteType ?? "")}
              badge={<ClayBadge tone={statusTone(st.status)} className="!px-2 !py-0.5 !text-[0.64rem]">{String(st.status ?? "Active")}</ClayBadge>}
            />
          ))}
        </RelSection>

        {comms.length > 0 && (
          <RelSection icon={MailWarning} title="Recent communications" count={comms.length} empty="">
            {comms.slice(0, 5).map((cm) => (
              <RelatedRow
                key={String(cm.id)}
                primary={String(cm.subject ?? cm.channel ?? "Message")}
                secondary={cm.sentAt ? String(cm.sentAt).slice(0, 16).replace("T", " ") : undefined}
              />
            ))}
          </RelSection>
        )}
      </div>

      {/* Recommendations — real ai_recommendations rows */}
      <section className="flex flex-col gap-2">
        <div className="flex items-center gap-2 px-1">
          <Sparkles size={14} strokeWidth={2.3} className="text-teal-700" aria-hidden />
          <h4 className="text-[0.72rem] font-bold uppercase tracking-[0.1em] text-slate-700">Recommendations</h4>
        </div>
        {recsQ.isLoading ? (
          <ClaySkeleton variant="text" lines={2} />
        ) : recs.length === 0 ? (
          <p className="px-3 py-1.5 text-[0.74rem] font-medium text-slate-600">
            No recommendations for this account.
          </p>
        ) : (
          <div className="flex flex-col gap-2">
            {recs.slice(0, 5).map((r) => (
              <ClayWell key={String(r.id)} padded className="flex flex-col gap-0.5">
                <div className="flex items-center gap-2">
                  <p className="min-w-0 flex-1 truncate text-[0.8rem] font-bold text-slate-800">{String(r.title ?? "Recommendation")}</p>
                  {r.score != null && (
                    <ClayBadge tone="info" className="!px-2 !py-0.5 !text-[0.64rem]">
                      {Math.round(Number(r.score))}
                    </ClayBadge>
                  )}
                </div>
                {Boolean(r.body) && <p className="text-[0.74rem] font-medium leading-snug text-slate-600">{String(r.body)}</p>}
              </ClayWell>
            ))}
          </div>
        )}
      </section>

      {/* Timeline — real audit / domain events */}
      <section className="flex flex-col gap-2">
        <div className="flex items-center gap-2 px-1">
          <Clock size={14} strokeWidth={2.3} className="text-teal-700" aria-hidden />
          <h4 className="text-[0.72rem] font-bold uppercase tracking-[0.1em] text-slate-700">Activity timeline</h4>
        </div>
        {timelineQ.isLoading ? (
          <ClaySkeleton variant="text" lines={3} />
        ) : timeline.length === 0 ? (
          <p className="px-3 py-1.5 text-[0.74rem] font-medium text-slate-600">No recorded activity yet.</p>
        ) : (
          <ol className="flex flex-col gap-2.5 border-l border-slate-200/80 pl-4">
            {timeline.slice(0, 12).map((ev, i) => (
              <li key={String(ev.id ?? i)} className="relative">
                <span
                  className="absolute -left-[1.42rem] top-1 size-2 rounded-full bg-teal-500 ring-2 ring-white"
                  aria-hidden
                />
                <p className="text-[0.78rem] font-semibold text-slate-800">{String(ev.title ?? ev.eventType ?? "Event")}</p>
                {Boolean(ev.body) && <p className="text-[0.72rem] font-medium leading-snug text-slate-600">{String(ev.body)}</p>}
                {(ev.eventTime as string) && (
                  <p className="mt-0.5 text-[0.66rem] font-medium tabular-nums text-slate-600">
                    {String(ev.eventTime).slice(0, 16).replace("T", " ")}
                  </p>
                )}
              </li>
            ))}
          </ol>
        )}
      </section>
    </ClayCard>
  );
}

// ── Bulk result banner — shows exactly what the API reported, per id ─────────────
function BulkResultBanner({
  result,
  nameById,
  onDismiss,
}: {
  result: BulkResponse;
  nameById: Map<string, string>;
  onDismiss: () => void;
}) {
  const failed = result.results.filter((r) => !r.ok);
  const allOk = result.failed === 0;
  return (
    <ClayCard
      className="shrink-0"
      rail={allOk ? "good" : "warn"}
      dense
      title={`Bulk ${result.action}: ${result.succeeded}/${result.requested} succeeded`}
      subtitle={allOk ? "Every selected customer was updated." : `${result.failed} could not be updated — details below.`}
      actions={<ClayButton size="sm" icon={X} aria-label="Dismiss results" onClick={onDismiss} />}
    >
      {!allOk && (
        <ul className="flex flex-col gap-1">
          {failed.map((r) => (
            <li key={r.id} className="flex items-center gap-2 text-[0.76rem]">
              <AlertTriangle size={13} strokeWidth={2.4} className="shrink-0 text-amber-600" aria-hidden />
              <span className="font-semibold text-slate-800">
                {nameById.get(String(r.id)) ?? `#${r.id}`}
              </span>
              <span className="text-slate-500">— {r.error ?? "Failed"}</span>
            </li>
          ))}
        </ul>
      )}
    </ClayCard>
  );
}

// ── Page ────────────────────────────────────────────────────────────────────────
type StatusFilter = "All" | CustomerStatus;
type TierFilter = "All" | CustomerSlaTier;

export function CustomersPage() {
  const can = useHasPermission();
  const canCreate = can(PERMISSIONS.CUSTOMERS_CREATE);
  const canUpdate = can(PERMISSIONS.CUSTOMERS_UPDATE);
  const canDelete = can(PERMISSIONS.CUSTOMERS_DELETE);

  const qc = useQueryClient();
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("All");
  const [tierFilter, setTierFilter] = useState<TierFilter>("All");
  const [search, setSearch] = useState("");
  const [selectedId, setSelectedId] = useState<string | number | null>(null);

  const [showCreate, setShowCreate] = useState(false);
  const [editRow, setEditRow] = useState<AnyRecord | null>(null);
  const [setValueKind, setSetValueKind] = useState<"status" | "tier" | null>(null);
  const [confirmKind, setConfirmKind] = useState<"bulk-delete" | "bulk-restore" | "row-delete" | null>(null);
  const [bulkResult, setBulkResult] = useState<BulkResponse | null>(null);

  const listQ = useQuery({ queryKey: ["customers", "list"], queryFn: customersApi.list, refetchInterval: 30_000 });
  const sumQ = useQuery({ queryKey: ["customers", "summary"], queryFn: customersApi.summary });

  const customers = (listQ.data ?? []) as AnyRecord[];
  const summary = sumQ.data;

  const filtered = useMemo(
    () =>
      customers.filter((c) => {
        if (statusFilter !== "All" && c.status !== statusFilter) return false;
        if (tierFilter !== "All" && c.slaTier !== tierFilter) return false;
        if (search) {
          const q = search.toLowerCase();
          return (
            String(c.name ?? "").toLowerCase().includes(q) ||
            String(c.customerCode ?? "").toLowerCase().includes(q) ||
            String(c.contactName ?? "").toLowerCase().includes(q)
          );
        }
        return true;
      }),
    [customers, statusFilter, tierFilter, search],
  );

  const visibleIds = useMemo(() => filtered.map((c) => c.id), [filtered]);
  const sel = useRowSelection(visibleIds);

  const nameById = useMemo(() => {
    const m = new Map<string, string>();
    for (const c of customers) m.set(String(c.id), String(c.name ?? `#${c.id}`));
    return m;
  }, [customers]);

  const selectedCustomer = useMemo(
    () => customers.find((c) => c.id === selectedId) ?? null,
    [customers, selectedId],
  );

  // Escape closes the detail panel (modals trap their own Escape).
  useEffect(() => {
    if (!selectedId) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") setSelectedId(null);
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [selectedId]);

  // ── Bulk mutation ─────────────────────────────────────────────────────────────
  const bulkMut = useMutation({
    mutationFn: (vars: { action: BulkAction; opts?: Parameters<typeof customersApi.bulk>[2] }) =>
      customersApi.bulk(vars.action, sel.selectedIds, vars.opts),
    onSuccess: (res) => {
      setBulkResult(res);
      sel.clear();
      setSetValueKind(null);
      setConfirmKind(null);
      void qc.invalidateQueries({ queryKey: ["customers"] });
    },
  });

  // ── Single-row delete (optimistic, rolls back on failure) ──────────────────────
  const rowDeleteMut = useMutation({
    mutationFn: (id: string | number) => customersApi.remove(id),
    onMutate: async (id) => {
      await qc.cancelQueries({ queryKey: ["customers", "list"] });
      const prev = qc.getQueryData<AnyRecord[]>(["customers", "list"]);
      qc.setQueryData<AnyRecord[]>(["customers", "list"], (old) =>
        (old ?? []).filter((c) => c.id !== id),
      );
      return { prev };
    },
    onError: (_err, _id, ctx) => {
      if (ctx?.prev) qc.setQueryData(["customers", "list"], ctx.prev);
    },
    onSuccess: () => {
      setSelectedId(null);
      setConfirmKind(null);
    },
    onSettled: () => {
      void qc.invalidateQueries({ queryKey: ["customers"] });
    },
  });

  if (listQ.isLoading) return <LoadingState />;
  if (listQ.isError) return <ErrorState message={(listQ.error as Error)?.message} onRetry={() => listQ.refetch()} />;

  const kpis: Array<{ label: string; value: ReactNode; unit?: string; icon: typeof Users; tone: ClayTone; hint?: string }> = [
    { label: "Total accounts", value: fmtInt(summary?.total), icon: Users, tone: "neutral" },
    { label: "Active", value: fmtInt(summary?.active), icon: ShieldCheck, tone: "good" },
    { label: "At risk", value: fmtInt(summary?.atRisk), icon: AlertTriangle, tone: "bad" },
    { label: "Avg SLA health", value: fmtInt(summary?.slaHealthScore), unit: summary?.slaHealthScore != null ? "%" : undefined, icon: Gauge, tone: "info" },
    { label: "Avg delivery exp.", value: fmtInt(summary?.deliveryExperienceScore), unit: summary?.deliveryExperienceScore != null ? "%" : undefined, icon: Activity, tone: "good" },
    { label: "Platinum accounts", value: fmtInt(summary?.platinumAccounts), icon: Crown, tone: "warn" },
  ];

  return (
    <div className="flex h-full min-h-0 flex-col gap-4 p-4 md:p-6">
      {/* Header */}
      <header className="flex shrink-0 flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-xl font-bold tracking-[-0.01em] text-slate-900">Customers</h1>
          <p className="mt-0.5 text-sm font-medium text-slate-500">
            Accounts, SLA health, delivery experience, and risk — wired into jobs, contracts, and sites.
          </p>
        </div>
        <div className="flex gap-2">
          <ClayButton variant="ghost" size="sm" icon={Download} onClick={() => exportCsv("customers", filtered)}>
            Export CSV
          </ClayButton>
          {canCreate && (
            <ClayButton variant="primary" size="sm" icon={Plus} onClick={() => setShowCreate(true)}>
              New customer
            </ClayButton>
          )}
        </div>
      </header>

      {/* KPI rail */}
      <div className="grid shrink-0 grid-cols-2 gap-3 sm:grid-cols-3 xl:grid-cols-6">
        {sumQ.isLoading
          ? Array.from({ length: 6 }, (_, i) => <ClayStatSkeleton key={i} />)
          : kpis.map((k) => (
              <ClayStat key={k.label} label={k.label} value={k.value} unit={k.unit} icon={k.icon} tone={k.tone} />
            ))}
      </div>

      {bulkResult && (
        <BulkResultBanner result={bulkResult} nameById={nameById} onDismiss={() => setBulkResult(null)} />
      )}

      {/* Master / detail split */}
      <div className="flex min-h-0 flex-1 gap-4">
        {/* Left: filters + table + bulk bar */}
        <div className="flex min-h-0 flex-1 flex-col gap-3">
          {/* Filter bar */}
          <ClayCard dense className="shrink-0" bodyClassName="flex flex-wrap items-center gap-3">
            <div className="flex flex-wrap gap-1.5">
              {(["All", ...STATUSES] as StatusFilter[]).map((f) => {
                const active = statusFilter === f;
                return (
                  <button
                    key={f}
                    type="button"
                    onClick={() => setStatusFilter(f)}
                    aria-pressed={active}
                    className={`cx-btn cx-btn-${active ? "primary" : "ghost"} px-3 py-1.5 text-[0.76rem]`}
                  >
                    {f}
                  </button>
                );
              })}
            </div>
            <ClaySelect
              aria-label="Filter by SLA tier"
              wrapperClassName="w-auto"
              value={tierFilter}
              onChange={(e) => setTierFilter(e.target.value as TierFilter)}
            >
              <option value="All">All tiers</option>
              {TIERS.map((t) => (
                <option key={t} value={t}>{t}</option>
              ))}
            </ClaySelect>
            <ClayInput
              icon={Search}
              type="search"
              aria-label="Search customers"
              placeholder="Search name, code, contact…"
              wrapperClassName="ml-auto w-full sm:w-64"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
          </ClayCard>

          {/* Table */}
          <ClayWell fill scroll className="min-h-0">
            {filtered.length === 0 ? (
              <div className="grid h-full place-items-center p-8">
                <EmptyState title="No customers match your filters" />
              </div>
            ) : (
              <table className="w-full border-collapse text-sm">
                <thead className="sticky top-0 z-10">
                  <tr className="bg-[var(--cx-bg-sunken)] shadow-[0_1px_0_rgba(148,163,184,.35)]">
                    <th className="w-10 px-3 py-2.5 text-left">
                      <BulkCheckbox
                        checked={sel.allVisibleSelected}
                        indeterminate={sel.someVisibleSelected}
                        onToggle={() => sel.toggleAllVisible()}
                        ariaLabel="Select all visible customers"
                      />
                    </th>
                    {["Customer", "Status", "Tier", "SLA health", "Delivery exp.", "Risk", "Jobs"].map((h) => (
                      <th
                        key={h}
                        className="px-3 py-2.5 text-left text-[0.66rem] font-bold uppercase tracking-[0.1em] text-slate-500"
                      >
                        {h}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {filtered.map((c) => {
                    const isSel = selectedId === c.id;
                    const isChecked = sel.isSelected(c.id);
                    return (
                      <tr
                        key={String(c.id)}
                        onClick={() => setSelectedId(isSel ? null : (c.id as string | number))}
                        className={`cursor-pointer border-b border-slate-200/60 transition-colors ${
                          isSel ? "bg-teal-500/10" : isChecked ? "bg-teal-500/[0.04]" : "hover:bg-white/50"
                        }`}
                      >
                        <td className="px-3 py-2.5" onClick={(e) => e.stopPropagation()}>
                          <BulkCheckbox
                            checked={isChecked}
                            onToggle={(shift) => sel.toggle(c.id, shift)}
                            ariaLabel={`Select ${String(c.name ?? c.id)}`}
                          />
                        </td>
                        <td className="px-3 py-2.5">
                          <p className="font-semibold text-slate-900">{String(c.name ?? "—")}</p>
                          <p className="text-[0.72rem] font-medium text-slate-600">
                            {String(c.customerCode ?? "")}
                            {c.contactName ? ` · ${String(c.contactName)}` : ""}
                          </p>
                        </td>
                        <td className="px-3 py-2.5">
                          <ClayBadge tone={statusTone(c.status)} dot className="!px-2 !py-0.5 !text-[0.66rem]">
                            {String(c.status ?? "Active")}
                          </ClayBadge>
                        </td>
                        <td className="px-3 py-2.5">
                          <ClayBadge tone={tierTone(c.slaTier)} className="!px-2 !py-0.5 !text-[0.66rem]">
                            {String(c.slaTier ?? "Standard")}
                          </ClayBadge>
                        </td>
                        <td className="px-3 py-2.5"><ScoreCell score={toScore(c.slaHealthScore)} /></td>
                        <td className="px-3 py-2.5">
                          <ScoreCell score={toScore(c.deliveryExperienceScore ?? c.customerDeliveryExperienceScore)} />
                        </td>
                        <td className="px-3 py-2.5">
                          <ClayBadge tone={riskTone(c.riskHeatScore)} className="!px-2 !py-0.5 !text-[0.66rem]">
                            {String(c.riskHeatScore ?? "Unrated")}
                          </ClayBadge>
                        </td>
                        <td className="px-3 py-2.5">
                          <span className="text-[0.8rem] font-bold tabular-nums text-slate-700">
                            {Number(c.activeJobs ?? 0) > 0 ? String(c.activeJobs) : "—"}
                          </span>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            )}
          </ClayWell>

          {/* Sticky bulk action bar */}
          <BulkBar count={sel.count} onClear={sel.clear}>
            {canUpdate && (
              <>
                <ClayButton size="sm" variant="ghost" icon={Activity} onClick={() => setSetValueKind("status")}>
                  Set status
                </ClayButton>
                <ClayButton size="sm" variant="ghost" icon={Tag} onClick={() => setSetValueKind("tier")}>
                  Set tier
                </ClayButton>
              </>
            )}
            {canDelete && (
              <>
                <ClayButton size="sm" variant="ghost" icon={RotateCcw} onClick={() => setConfirmKind("bulk-restore")}>
                  Restore
                </ClayButton>
                <ClayButton size="sm" variant="danger" icon={Trash2} onClick={() => setConfirmKind("bulk-delete")}>
                  Delete
                </ClayButton>
              </>
            )}
          </BulkBar>
        </div>

        {/* Right: inline detail panel (overlay on < xl) */}
        {selectedCustomer && (
          <>
            <button
              type="button"
              aria-label="Close detail panel"
              className="fixed inset-0 z-30 bg-slate-950/40 xl:hidden"
              onClick={() => setSelectedId(null)}
            />
            <aside className="fixed inset-y-0 right-0 z-40 flex w-full max-w-md flex-col p-4 xl:static xl:z-auto xl:w-[26rem] xl:max-w-none xl:shrink-0 xl:p-0">
              <DetailPanel
                customer={selectedCustomer}
                onClose={() => setSelectedId(null)}
                onEdit={() => setEditRow(selectedCustomer)}
                onDelete={() => setConfirmKind("row-delete")}
                canUpdate={canUpdate}
                canDelete={canDelete}
              />
            </aside>
          </>
        )}
      </div>

      {/* ── Modals & dialogs ──────────────────────────────────────────────────── */}
      {showCreate && (
        <CustomerForm mode="create" onClose={() => setShowCreate(false)} onSaved={() => setShowCreate(false)} />
      )}
      {editRow && (
        <CustomerForm
          mode="edit"
          initial={editRow}
          onClose={() => setEditRow(null)}
          onSaved={() => setEditRow(null)}
        />
      )}
      {setValueKind && (
        <SetValueModal
          kind={setValueKind}
          count={sel.count}
          busy={bulkMut.isPending}
          onClose={() => setSetValueKind(null)}
          onApply={(value) =>
            bulkMut.mutate(
              setValueKind === "status"
                ? { action: "set-status", opts: { patch: { status: value as CustomerStatus } } }
                : { action: "set-tier", opts: { patch: { slaTier: value as CustomerSlaTier } } },
            )
          }
        />
      )}

      <ConfirmDialog
        open={confirmKind === "bulk-delete"}
        title={`Delete ${sel.count} ${sel.count === 1 ? "customer" : "customers"}?`}
        body={
          <>
            This soft-deletes the selected {sel.count === 1 ? "account" : "accounts"} — they can be
            restored later. Type <span className="font-mono">DELETE</span> to confirm.
          </>
        }
        confirmText="DELETE"
        confirmLabel="Delete customers"
        busy={bulkMut.isPending}
        onClose={() => setConfirmKind(null)}
        onConfirm={() => bulkMut.mutate({ action: "delete", opts: { confirm: "DELETE" } })}
      />
      <ConfirmDialog
        open={confirmKind === "bulk-restore"}
        title={`Restore ${sel.count} ${sel.count === 1 ? "customer" : "customers"}?`}
        body="Restored accounts return to Active status."
        confirmLabel="Restore"
        danger={false}
        busy={bulkMut.isPending}
        onClose={() => setConfirmKind(null)}
        onConfirm={() => bulkMut.mutate({ action: "restore" })}
      />
      <ConfirmDialog
        open={confirmKind === "row-delete"}
        title={`Delete ${selectedCustomer?.name ?? "customer"}?`}
        body={
          <>
            This soft-deletes the account. Type <span className="font-mono">DELETE</span> to confirm.
            {rowDeleteMut.isError && (
              <span className="mt-2 block font-semibold text-red-700">
                {(rowDeleteMut.error as Error)?.message ?? "Delete failed."}
              </span>
            )}
          </>
        }
        confirmText="DELETE"
        confirmLabel="Delete customer"
        busy={rowDeleteMut.isPending}
        onClose={() => setConfirmKind(null)}
        onConfirm={() => selectedCustomer && rowDeleteMut.mutate(selectedCustomer.id as string | number)}
      />
    </div>
  );
}
