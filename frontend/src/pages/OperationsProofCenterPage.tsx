import { type FormEvent, type ReactNode, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  ArrowRight,
  CheckCircle2,
  RefreshCw,
  ShieldCheck,
  Sparkles,
  TriangleAlert,
  Truck,
} from "lucide-react";
import { KpiCard, LoadingState, PageHeader, RiskBadge, StatusBadge, labelize } from "@/components/ui";
import { useHasPermission } from "@/hooks/usePermission";
import { operationsProofApi } from "@/services/operationsProofApi";
import type { AnyRecord } from "@/types";

type FormState = Record<string, string>;

const empty = (value: unknown) => String(value ?? "");

function SectionCard({
  title,
  eyebrow,
  status,
  risk,
  children,
  actions,
}: {
  title: string;
  eyebrow: string;
  status?: string;
  risk?: string;
  children: ReactNode;
  actions?: React.ReactNode;
}) {
  return (
    <section className="panel overflow-hidden p-5">
      <div className="flex flex-col gap-3 border-b border-slate-100 pb-4 lg:flex-row lg:items-start lg:justify-between">
        <div>
          <p className="text-[10px] font-bold uppercase tracking-[0.28em] text-teal-500">{eyebrow}</p>
          <h2 className="mt-1 text-lg font-bold tracking-tight text-slate-900">{title}</h2>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {status ? <StatusBadge status={status} /> : null}
          {risk ? <RiskBadge risk={risk} /> : null}
          {actions}
        </div>
      </div>
      <div className="mt-4">{children}</div>
    </section>
  );
}

function Field({
  label,
  value,
}: {
  label: string;
  value: unknown;
}) {
  return (
    <div className="rounded-2xl border border-slate-200 bg-slate-50/70 p-3">
      <p className="text-[10px] font-bold uppercase tracking-[0.24em] text-slate-400">{label}</p>
      <p className="mt-1 text-sm font-medium text-slate-900">{value ? String(value) : "No data yet"}</p>
    </div>
  );
}

function JSONSummary({ value }: { value: unknown }) {
  if (value == null) {
    return <p className="text-sm text-slate-400">No data yet.</p>;
  }

  if (Array.isArray(value)) {
    if (value.length === 0) return <p className="text-sm text-slate-400">No data yet.</p>;
    return (
      <ul className="space-y-2 text-sm text-slate-700">
        {value.map((item, index) => (
          <li key={index} className="rounded-xl border border-slate-200 bg-white/80 px-3 py-2">
            {typeof item === "string" ? item : JSON.stringify(item)}
          </li>
        ))}
      </ul>
    );
  }

  if (typeof value === "object") {
    const entries = Object.entries(value as AnyRecord);
    if (entries.length === 0) return <p className="text-sm text-slate-400">No data yet.</p>;
    return (
      <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
        {entries.map(([key, item]) => (
          <Field key={key} label={labelize(key)} value={typeof item === "object" ? JSON.stringify(item) : item} />
        ))}
      </div>
    );
  }

  return <p className="text-sm text-slate-700">{String(value)}</p>;
}

export function OperationsProofCenterPage() {
  const hasPermission = useHasPermission();
  const qc = useQueryClient();
  const [jobInput, setJobInput] = useState("1201");
  const [jobId, setJobId] = useState<number | null>(1201);
  const [forms, setForms] = useState<Record<string, FormState>>({
    siteAccess: { requirementType: "gate_pass", instructions: "Verify access with the site desk." },
    accessDocument: { documentType: "noc", documentNo: "NOC-1001" },
    pickupAuthorization: { authorizationNo: "PICK-1001", thirdPartyName: "Alpha Logistics" },
    warehouseHandover: { handoverType: "warehouse_handover", warehouseName: "Main Warehouse" },
    proofPackage: { proofType: "proof_of_delivery", receiverName: "Receiver Name", receiverPhone: "" },
    proofArtifact: { artifactType: "photo", notes: "Captured in the field" },
    smartAssignment: { recommendedDriverId: "", recommendedVehicleId: "", score: "0.78", confidenceScore: "0.72", riskLevel: "medium" },
  });

  const summaryQuery = useQuery({
    queryKey: ["operations", "execution-summary", jobId],
    queryFn: () => operationsProofApi.executionSummary(jobId ?? 0),
    enabled: jobId !== null,
    staleTime: 20_000,
  });

  const summary = summaryQuery.data as AnyRecord | undefined;
  const assignment = summary?.smart_assignment_summary as AnyRecord | undefined;
  const siteAccess = summary?.site_access_summary as AnyRecord | undefined;
  const accessDocument = summary?.access_document_summary as AnyRecord | undefined;
  const pickupAuthorization = summary?.pickup_authorization_summary as AnyRecord | undefined;
  const warehouseHandover = summary?.warehouse_handover_summary as AnyRecord | undefined;
  const proofPackage = summary?.proof_package_summary as AnyRecord | undefined;
  const proofArtifact = summary?.proof_artifact_summary as AnyRecord | undefined;
  const billingConfidence = summary?.billing_confidence_summary as AnyRecord | undefined;
  const riskSummary = summary?.risk_summary as AnyRecord | undefined;
  const validationSummary = summary?.validation_summary as AnyRecord | undefined;
  const nextBestActions = summary?.next_best_actions as AnyRecord[] | undefined;
  const mobileReadyActions = summary?.mobile_ready_actions as AnyRecord[] | undefined;
  const latestRecommendation = (assignment?.latest as AnyRecord | undefined) ?? (assignment?.latest_recommendation as AnyRecord | undefined);
  const latestRecommendationId = Number(latestRecommendation?.id ?? 0);
  const latestProofPackage = (proofPackage?.latest as AnyRecord | undefined) ?? undefined;
  const latestProofPackageId = Number(latestProofPackage?.id ?? 0);

  const refresh = () => {
    if (jobId !== null) {
      void qc.invalidateQueries({ queryKey: ["operations", "execution-summary", jobId] });
    }
  };

  const smartAssignMutation = useMutation({
    mutationFn: async (mode: "accept" | "reject") => {
      if (!latestRecommendationId) throw new Error("No recommendation is available yet.");
      return mode === "accept"
        ? operationsProofApi.acceptSmartAssignment(latestRecommendationId, {})
        : operationsProofApi.rejectSmartAssignment(latestRecommendationId, {});
    },
    onSuccess: refresh,
  });

  const createSiteAccessMutation = useMutation({
    mutationFn: () => {
      if (jobId === null) throw new Error("Enter a job id first.");
      return operationsProofApi.createSiteAccess(jobId, forms.siteAccess);
    },
    onSuccess: refresh,
  });

  const createAccessDocumentMutation = useMutation({
    mutationFn: () => {
      if (jobId === null) throw new Error("Enter a job id first.");
      return operationsProofApi.createAccessDocument(jobId, forms.accessDocument);
    },
    onSuccess: refresh,
  });

  const createPickupAuthorizationMutation = useMutation({
    mutationFn: () => {
      if (jobId === null) throw new Error("Enter a job id first.");
      return operationsProofApi.createPickupAuthorization(jobId, forms.pickupAuthorization);
    },
    onSuccess: refresh,
  });

  const createWarehouseHandoverMutation = useMutation({
    mutationFn: () => {
      if (jobId === null) throw new Error("Enter a job id first.");
      return operationsProofApi.createWarehouseHandover(jobId, forms.warehouseHandover);
    },
    onSuccess: refresh,
  });

  const createProofPackageMutation = useMutation({
    mutationFn: () => {
      if (jobId === null) throw new Error("Enter a job id first.");
      return operationsProofApi.createProofPackage(jobId, forms.proofPackage);
    },
    onSuccess: refresh,
  });

  const submitProofPackageMutation = useMutation({
    mutationFn: async () => {
      if (!latestProofPackageId) throw new Error("Create a proof package first.");
      return operationsProofApi.submitProofPackage(latestProofPackageId, {});
    },
    onSuccess: refresh,
  });

  const validateProofPackageMutation = useMutation({
    mutationFn: async () => {
      if (!latestProofPackageId) throw new Error("Create a proof package first.");
      return operationsProofApi.validateProofPackage(latestProofPackageId, {});
    },
    onSuccess: refresh,
  });

  const createProofArtifactMutation = useMutation({
    mutationFn: async () => {
      if (!latestProofPackageId) throw new Error("Create a proof package first.");
      return operationsProofApi.createProofArtifact(latestProofPackageId, forms.proofArtifact);
    },
    onSuccess: refresh,
  });

  const summaryStatus = empty(riskSummary?.status ?? validationSummary?.status ?? "No data yet");
  const blockers = (riskSummary?.open_blockers as string[] | undefined) ?? [];

  const loadJob = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const parsed = Number.parseInt(jobInput, 10);
    setJobId(Number.isFinite(parsed) && parsed > 0 ? parsed : null);
  };

  const updateForm = (section: string, key: string, value: string) => {
    setForms((current) => ({
      ...current,
      [section]: {
        ...(current[section] ?? {}),
        [key]: value,
      },
    }));
  };

  return (
    <div className="control-tower space-y-8">
      <PageHeader
        eyebrow="Operational Proof"
        title="Operational Proof Center"
        description="A live read-model view of assignment, access, handover, proof, and billing confidence using Stage 9 persistence. This is a read-model first surface, not a mobile app."
        actions={
          <>
            <form className="flex items-center gap-2" onSubmit={loadJob}>
              <input
                className="field h-10 w-36"
                value={jobInput}
                onChange={(event) => setJobInput(event.target.value)}
                inputMode="numeric"
                placeholder="Job ID"
              />
              <button className="btn-primary" type="submit">
                <RefreshCw className="h-4 w-4" />
                Load
              </button>
            </form>
          </>
        }
      />

      {summaryQuery.isLoading ? (
        <LoadingState />
      ) : summaryQuery.isError ? (
        <div className="panel border border-rose-200 bg-rose-50/70 p-5 text-rose-700">
          Failed to load execution summary. Check the backend connection and RBAC permission.
        </div>
      ) : !summary ? (
        <section className="panel p-6">
          <p className="text-sm text-slate-500">Enter a job id to view execution data. No synthetic data is being injected here.</p>
        </section>
      ) : (
        <>
          <div className="grid gap-4 lg:grid-cols-4">
            <KpiCard label="Risk Status" value={summaryStatus} icon={<TriangleAlert />} status={summaryStatus} />
            <KpiCard label="Next Best Actions" value={String(nextBestActions?.length ?? 0)} icon={<Sparkles />} status="Active" />
            <KpiCard label="Mobile Ready Actions" value={String(mobileReadyActions?.length ?? 0)} icon={<ShieldCheck />} status="Active" />
            <KpiCard label="Billing Confidence" value={String(billingConfidence?.confidence_score ?? "No data")} icon={<CheckCircle2 />} status={billingConfidence?.status as string | undefined} />
          </div>

          <section className="panel p-5">
            <div className="grid gap-4 lg:grid-cols-4">
              <Field label="Job ID" value={summary.job_id} />
              <Field label="Trip ID" value={summary.trip_id ?? "No data yet"} />
              <Field label="Open Blockers" value={blockers.length > 0 ? blockers.join(", ") : "None"} />
              <Field label="Validation" value={empty(validationSummary?.status ?? "No data yet")} />
            </div>
          </section>

          <SectionCard
            eyebrow="Smart Assignment"
            title="Dispatcher / Supervisor assignment decision"
            status={assignment?.status as string | undefined}
            risk={assignment?.risk_level as string | undefined}
            actions={latestRecommendationId && hasPermission("dispatch.smart_assign.accept") ? (
              <div className="flex items-center gap-2">
                <button
                  type="button"
                  className="btn-ghost"
                  onClick={() => smartAssignMutation.mutate("reject")}
                  disabled={smartAssignMutation.isPending}
                >
                  Reject
                </button>
                <button
                  type="button"
                  className="btn-primary"
                  onClick={() => smartAssignMutation.mutate("accept")}
                  disabled={smartAssignMutation.isPending}
                >
                  Accept
                  <ArrowRight className="h-4 w-4" />
                </button>
              </div>
            ) : null}
          >
            <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
              <Field label="Score" value={assignment?.score ?? "No data yet"} />
              <Field label="Confidence" value={assignment?.confidence_score ?? assignment?.confidenceScore ?? "No data yet"} />
              <Field label="Recommended Driver" value={empty(latestRecommendation?.recommendedDriverId ?? "No data yet")} />
              <Field label="Recommended Vehicle" value={empty(latestRecommendation?.recommendedVehicleId ?? "No data yet")} />
            </div>
            <div className="mt-4 grid gap-4 lg:grid-cols-2">
              <div>
                <p className="text-xs font-bold uppercase tracking-[0.24em] text-slate-400">Reasons / Constraints</p>
                <div className="mt-2">
                  <JSONSummary value={latestRecommendation?.reasonJson ?? latestRecommendation?.constraintJson ?? "No data yet"} />
                </div>
              </div>
              <div>
                <p className="text-xs font-bold uppercase tracking-[0.24em] text-slate-400">Mobile-safe action posture</p>
                <div className="mt-2 rounded-2xl border border-slate-200 bg-slate-50/60 p-3 text-sm text-slate-600">
                  AI content is recommendation-only. Acceptance is gated by backend permission and cannot directly mutate business tables outside the approved workflow.
                </div>
              </div>
            </div>
          </SectionCard>

          <SectionCard eyebrow="Site Access / Gate Pass / NOC" title="Access controls before execution" status={siteAccess?.status as string | undefined}>
            <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
              <Field label="Requirement Type" value={(siteAccess?.latest_requirement as AnyRecord | undefined)?.requirementType ?? "No data yet"} />
              <Field label="Required Before" value={(siteAccess?.latest_requirement as AnyRecord | undefined)?.requiredBefore ?? "No data yet"} />
              <Field label="Instructions" value={(siteAccess?.latest_requirement as AnyRecord | undefined)?.instructions ?? "No data yet"} />
              <Field label="Contact" value={(siteAccess?.latest_requirement as AnyRecord | undefined)?.contactName ?? "No data yet"} />
            </div>
            <div className="mt-4 grid gap-3 md:grid-cols-2 xl:grid-cols-4">
              <input className="field" value={forms.siteAccess.requirementType ?? ""} onChange={(event) => updateForm("siteAccess", "requirementType", event.target.value)} placeholder="Requirement type" />
              <input className="field" value={forms.siteAccess.contactName ?? ""} onChange={(event) => updateForm("siteAccess", "contactName", event.target.value)} placeholder="Contact name" />
              <input className="field" value={forms.siteAccess.contactPhone ?? ""} onChange={(event) => updateForm("siteAccess", "contactPhone", event.target.value)} placeholder="Contact phone" />
              <textarea className="field min-h-24 md:col-span-2 xl:col-span-4" value={forms.siteAccess.instructions ?? ""} onChange={(event) => updateForm("siteAccess", "instructions", event.target.value)} placeholder="Instructions" />
            </div>
            <div className="mt-4 flex flex-wrap gap-2">
              {hasPermission("operations.site_access.create") ? (
                <button type="button" className="btn-primary" onClick={() => createSiteAccessMutation.mutate()} disabled={createSiteAccessMutation.isPending}>
                  Create Requirement
                </button>
              ) : null}
            </div>
          </SectionCard>

          <SectionCard eyebrow="Access Document" title="Gate pass or NOC document" status={accessDocument?.status as string | undefined}>
            <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
              <Field label="Document Type" value={(accessDocument?.latest as AnyRecord | undefined)?.documentType ?? "No data yet"} />
              <Field label="Document No" value={(accessDocument?.latest as AnyRecord | undefined)?.documentNo ?? "No data yet"} />
              <Field label="Valid To" value={(accessDocument?.latest as AnyRecord | undefined)?.validTo ?? "No data yet"} />
              <Field label="Verification" value={(accessDocument?.latest as AnyRecord | undefined)?.status ?? "No data yet"} />
            </div>
            <div className="mt-4 grid gap-3 md:grid-cols-2 xl:grid-cols-4">
              <input className="field" value={forms.accessDocument.documentType ?? ""} onChange={(event) => updateForm("accessDocument", "documentType", event.target.value)} placeholder="Document type" />
              <input className="field" value={forms.accessDocument.documentNo ?? ""} onChange={(event) => updateForm("accessDocument", "documentNo", event.target.value)} placeholder="Document no" />
              <input className="field" value={forms.accessDocument.status ?? "required"} onChange={(event) => updateForm("accessDocument", "status", event.target.value)} placeholder="Status" />
              <input className="field" value={forms.accessDocument.notes ?? ""} onChange={(event) => updateForm("accessDocument", "notes", event.target.value)} placeholder="Notes" />
            </div>
            <div className="mt-4 flex flex-wrap gap-2">
              {hasPermission("operations.access_document.create") ? (
                <button type="button" className="btn-primary" onClick={() => createAccessDocumentMutation.mutate()} disabled={createAccessDocumentMutation.isPending}>
                  Create Document
                </button>
              ) : null}
            </div>
          </SectionCard>

          <SectionCard eyebrow="Third-Party Pickup Authorization" title="Pickup release and verification" status={pickupAuthorization?.status as string | undefined}>
            <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
              <Field label="Authorization No" value={(pickupAuthorization?.latest as AnyRecord | undefined)?.authorizationNo ?? "No data yet"} />
              <Field label="Third Party" value={(pickupAuthorization?.latest as AnyRecord | undefined)?.thirdPartyName ?? "No data yet"} />
              <Field label="Authorized Person" value={(pickupAuthorization?.latest as AnyRecord | undefined)?.authorizedPersonName ?? "No data yet"} />
              <Field label="Validity" value={`${empty((pickupAuthorization?.latest as AnyRecord | undefined)?.validFrom ?? "No data")} → ${empty((pickupAuthorization?.latest as AnyRecord | undefined)?.validTo ?? "No data")}`} />
            </div>
            <div className="mt-4 grid gap-3 md:grid-cols-2 xl:grid-cols-4">
              <input className="field" value={forms.pickupAuthorization.authorizationNo ?? ""} onChange={(event) => updateForm("pickupAuthorization", "authorizationNo", event.target.value)} placeholder="Authorization no" />
              <input className="field" value={forms.pickupAuthorization.thirdPartyName ?? ""} onChange={(event) => updateForm("pickupAuthorization", "thirdPartyName", event.target.value)} placeholder="Third-party name" />
              <input className="field" value={forms.pickupAuthorization.authorizedPersonName ?? ""} onChange={(event) => updateForm("pickupAuthorization", "authorizedPersonName", event.target.value)} placeholder="Authorized person" />
              <input className="field" value={forms.pickupAuthorization.status ?? "required"} onChange={(event) => updateForm("pickupAuthorization", "status", event.target.value)} placeholder="Status" />
            </div>
            <div className="mt-4 flex flex-wrap gap-2">
              {hasPermission("operations.pickup_authorization.create") ? (
                <button type="button" className="btn-primary" onClick={() => createPickupAuthorizationMutation.mutate()} disabled={createPickupAuthorizationMutation.isPending}>
                  Create Authorization
                </button>
              ) : null}
            </div>
          </SectionCard>

          <SectionCard eyebrow="Warehouse Handover" title="Warehouse to field handover" status={warehouseHandover?.status as string | undefined}>
            <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
              <Field label="Warehouse" value={(warehouseHandover?.latest as AnyRecord | undefined)?.warehouseName ?? "No data yet"} />
              <Field label="Reference" value={(warehouseHandover?.latest as AnyRecord | undefined)?.warehouseReferenceNo ?? "No data yet"} />
              <Field label="Handover Type" value={(warehouseHandover?.latest as AnyRecord | undefined)?.handoverType ?? "No data yet"} />
              <Field label="Completed" value={(warehouseHandover?.latest as AnyRecord | undefined)?.completedAt ?? "No data yet"} />
            </div>
            <div className="mt-4 grid gap-3 md:grid-cols-2 xl:grid-cols-4">
              <input className="field" value={forms.warehouseHandover.warehouseName ?? ""} onChange={(event) => updateForm("warehouseHandover", "warehouseName", event.target.value)} placeholder="Warehouse name" />
              <input className="field" value={forms.warehouseHandover.warehouseReferenceNo ?? ""} onChange={(event) => updateForm("warehouseHandover", "warehouseReferenceNo", event.target.value)} placeholder="Reference no" />
              <input className="field" value={forms.warehouseHandover.handoverType ?? "warehouse_handover"} onChange={(event) => updateForm("warehouseHandover", "handoverType", event.target.value)} placeholder="Handover type" />
              <input className="field" value={forms.warehouseHandover.status ?? "scheduled"} onChange={(event) => updateForm("warehouseHandover", "status", event.target.value)} placeholder="Status" />
            </div>
            <div className="mt-4 flex flex-wrap gap-2">
              {hasPermission("operations.warehouse_handover.create") ? (
                <button type="button" className="btn-primary" onClick={() => createWarehouseHandoverMutation.mutate()} disabled={createWarehouseHandoverMutation.isPending}>
                  Record Handover
                </button>
              ) : null}
            </div>
          </SectionCard>

          <SectionCard
            eyebrow="POD / Proof Package"
            title="Proof package lifecycle"
            status={proofPackage?.status as string | undefined}
            risk={proofPackage?.validation_status as string | undefined}
            actions={latestProofPackageId && (
              <div className="flex items-center gap-2">
                {hasPermission("operations.proof.submit") ? (
                  <button type="button" className="btn-ghost" onClick={() => submitProofPackageMutation.mutate()} disabled={submitProofPackageMutation.isPending}>
                    Submit
                  </button>
                ) : null}
                {hasPermission("operations.proof.validate") ? (
                  <button type="button" className="btn-primary" onClick={() => validateProofPackageMutation.mutate()} disabled={validateProofPackageMutation.isPending}>
                    Validate
                  </button>
                ) : null}
              </div>
            )}
          >
            <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
              <Field label="Proof Type" value={(proofPackage?.latest as AnyRecord | undefined)?.proofType ?? "No data yet"} />
              <Field label="Receiver" value={(proofPackage?.latest as AnyRecord | undefined)?.receiverName ?? "No data yet"} />
              <Field label="Validation Status" value={proofPackage?.validation_status ?? "No data yet"} />
              <Field label="Notes" value={(proofPackage?.latest as AnyRecord | undefined)?.notes ?? "No data yet"} />
            </div>
            <div className="mt-4 grid gap-3 md:grid-cols-2 xl:grid-cols-4">
              <input className="field" value={forms.proofPackage.proofType ?? ""} onChange={(event) => updateForm("proofPackage", "proofType", event.target.value)} placeholder="Proof type" />
              <input className="field" value={forms.proofPackage.receiverName ?? ""} onChange={(event) => updateForm("proofPackage", "receiverName", event.target.value)} placeholder="Receiver name" />
              <input className="field" value={forms.proofPackage.receiverPhone ?? ""} onChange={(event) => updateForm("proofPackage", "receiverPhone", event.target.value)} placeholder="Receiver phone" />
              <input className="field" value={forms.proofPackage.status ?? "draft"} onChange={(event) => updateForm("proofPackage", "status", event.target.value)} placeholder="Status" />
            </div>
            <div className="mt-4 flex flex-wrap gap-2">
              {hasPermission("operations.proof.create") ? (
                <button type="button" className="btn-primary" onClick={() => createProofPackageMutation.mutate()} disabled={createProofPackageMutation.isPending}>
                  Create Proof Package
                </button>
              ) : null}
            </div>
          </SectionCard>

          <SectionCard eyebrow="Evidence Artifacts" title="Attached proof artifacts" status={proofArtifact?.status as string | undefined}>
            <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
              <Field label="Artifact Type" value={(proofArtifact?.latest as AnyRecord | undefined)?.artifactType ?? "No data yet"} />
              <Field label="Captured By" value={(proofArtifact?.latest as AnyRecord | undefined)?.capturedByUserId ?? "No data yet"} />
              <Field label="Device ID" value={(proofArtifact?.latest as AnyRecord | undefined)?.deviceId ?? "No data yet"} />
              <Field label="Captured At" value={(proofArtifact?.latest as AnyRecord | undefined)?.capturedAt ?? "No data yet"} />
            </div>
            <div className="mt-4 grid gap-3 md:grid-cols-2 xl:grid-cols-4">
              <input className="field" value={forms.proofArtifact.artifactType ?? ""} onChange={(event) => updateForm("proofArtifact", "artifactType", event.target.value)} placeholder="Artifact type" />
              <input className="field" value={forms.proofArtifact.deviceId ?? ""} onChange={(event) => updateForm("proofArtifact", "deviceId", event.target.value)} placeholder="Device ID" />
              <input className="field" value={forms.proofArtifact.capturedByUserId ?? ""} onChange={(event) => updateForm("proofArtifact", "capturedByUserId", event.target.value)} placeholder="Captured by user" />
              <textarea className="field min-h-24 md:col-span-2 xl:col-span-4" value={forms.proofArtifact.notes ?? ""} onChange={(event) => updateForm("proofArtifact", "notes", event.target.value)} placeholder="Artifact notes" />
            </div>
            <div className="mt-4 flex flex-wrap gap-2">
              {hasPermission("operations.proof_artifact.create") ? (
                <button type="button" className="btn-primary" onClick={() => createProofArtifactMutation.mutate()} disabled={createProofArtifactMutation.isPending}>
                  Add Artifact Metadata
                </button>
              ) : null}
            </div>
          </SectionCard>

          <SectionCard eyebrow="Billing Confidence" title="Billing trust indicator" status={billingConfidence?.status as string | undefined} risk={riskSummary?.status as string | undefined}>
            <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
              <Field label="Confidence Score" value={billingConfidence?.confidence_score ?? billingConfidence?.confidenceScore ?? "No data yet"} />
              <Field label="Summary" value={billingConfidence?.summary ?? "No data yet"} />
              <Field label="Blocked" value={blockers.length > 0 ? "Yes" : "No"} />
              <Field label="Billing Ready" value={riskSummary?.billing_ready ? "Yes" : "No"} />
            </div>
            {blockers.length > 0 ? (
              <div className="mt-4 rounded-2xl border border-amber-200 bg-amber-50/70 p-4 text-sm text-amber-900">
                <p className="font-semibold">Current blockers</p>
                <ul className="mt-2 list-disc space-y-1 pl-5">
                  {blockers.map((item) => <li key={item}>{item}</li>)}
                </ul>
              </div>
            ) : (
              <div className="mt-4 rounded-2xl border border-emerald-200 bg-emerald-50/70 p-4 text-sm text-emerald-900">
                Billing confidence is clean enough for operator review, but no invoice is issued automatically.
              </div>
            )}
          </SectionCard>

          <section className="panel p-5">
            <div className="flex items-center gap-2">
              <Truck className="h-4 w-4 text-teal-500" />
              <h2 className="text-lg font-bold text-slate-900">Mobile Readiness Preview</h2>
            </div>
            <p className="mt-2 text-sm text-slate-500">
              This is a preview of the future mobile contract, not a native app. It points to the same backend contract surface this stage hardens.
            </p>
            <div className="mt-4 overflow-hidden rounded-2xl border border-slate-200">
              <table className="w-full text-left text-sm">
                <thead className="bg-slate-50 text-[11px] uppercase tracking-[0.22em] text-slate-500">
                  <tr>
                    <th className="px-4 py-3">Role</th>
                    <th className="px-4 py-3">Route family</th>
                    <th className="px-4 py-3">Permission</th>
                    <th className="px-4 py-3">Offline / Evidence</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100 bg-white">
                  {(mobileReadyActions ?? []).map((row, index) => (
                    <tr key={index}>
                      <td className="px-4 py-3 font-medium text-slate-900">{String(row.role ?? "No data yet")}</td>
                      <td className="px-4 py-3 text-slate-600">{String(row.route_family ?? "No data yet")}</td>
                      <td className="px-4 py-3 text-slate-600">{String(row.permission ?? "No data yet")}</td>
                      <td className="px-4 py-3 text-slate-600">
                        {row.offline_ready ? "Offline-ready contract" : "Online-only"} / {row.evidence_ready ? "Evidence ready" : "Evidence pending"}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>
        </>
      )}
    </div>
  );
}
