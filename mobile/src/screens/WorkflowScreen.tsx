import { useMemo } from "react";
import { Alert, View } from "react-native";
import {
  ActionButton,
  EmptyState,
  ErrorState,
  Field,
  HeroPanel,
  LoadingState,
  Panel,
  Pill,
  Row,
  Screen,
  SectionHeader,
} from "@/components/ui";
import { useSession } from "@/auth/SessionProvider";
import { useWorkflow } from "@/workflow/WorkflowContext";
import { useAsyncResource } from "@/hooks/useAsyncResource";
import type { JsonRecord } from "@/types";

function asArray(value: unknown): JsonRecord[] {
  if (Array.isArray(value)) return value as JsonRecord[];
  if (value && typeof value === "object") {
    const record = value as Record<string, unknown>;
    for (const key of ["items", "rows", "data", "results", "latest"]) {
      if (Array.isArray(record[key])) return record[key] as JsonRecord[];
    }
  }
  return [];
}

function textOf(value: unknown) {
  return value === null || value === undefined || value === "" ? "No data yet" : String(value);
}

export function WorkflowScreen() {
  const { api, hasPermission } = useSession();
  const { selectedJobId, bumpRefreshKey, refreshKey } = useWorkflow();
  const hasAnyPermission = (...permissions: string[]) => permissions.some(hasPermission);
  const canReadRecommendations = hasAnyPermission("dispatch.smart_assign.read", "dispatch.smart_assign.recommend", "dispatch.smart_assign.accept", "dispatch.smart_assign.reject");
  const canReadSiteAccess = hasAnyPermission("operations.site_access.read", "operations.site_access.create", "operations.site_access.update", "dispatch:view", "dispatch:manage", "job.update");
  const canReadPickup = hasAnyPermission("operations.pickup_authorization.read", "operations.pickup_authorization.create", "operations.pickup_authorization.update", "operations.pickup_authorization.verify", "dispatch:view", "dispatch:manage", "driver:self");
  const canReadHandovers = hasAnyPermission("operations.warehouse_handover.read", "operations.warehouse_handover.create", "operations.warehouse_handover.update", "dispatch:view", "dispatch:manage", "driver:self");
  const canReadProof = hasAnyPermission("operations.proof.read", "operations.proof.create", "operations.proof.update", "operations.proof.submit", "operations.proof.validate");

  const recommendations = useAsyncResource(
    async () => (selectedJobId && canReadRecommendations ? api.smartAssignmentRecommendations(selectedJobId) : null),
    [api, selectedJobId, canReadRecommendations, refreshKey],
  );
  const siteAccess = useAsyncResource(async () => (selectedJobId && canReadSiteAccess ? api.siteAccess(selectedJobId) : null), [api, selectedJobId, canReadSiteAccess, refreshKey]);
  const pickupAuthorizations = useAsyncResource(async () => (selectedJobId && canReadPickup ? api.pickupAuthorizations(selectedJobId) : null), [api, selectedJobId, canReadPickup, refreshKey]);
  const handovers = useAsyncResource(async () => (selectedJobId && canReadHandovers ? api.warehouseHandovers(selectedJobId) : null), [api, selectedJobId, canReadHandovers, refreshKey]);
  const proofPackages = useAsyncResource(async () => (selectedJobId && canReadProof ? api.proofPackages(selectedJobId) : null), [api, selectedJobId, canReadProof, refreshKey]);

  const latestRecommendation = useMemo(() => asArray(recommendations.data)[0] ?? null, [recommendations.data]);
  const latestSiteAccess = useMemo(() => asArray(siteAccess.data)[0] ?? null, [siteAccess.data]);
  const latestPickupAuthorization = useMemo(() => asArray(pickupAuthorizations.data)[0] ?? null, [pickupAuthorizations.data]);
  const latestHandover = useMemo(() => asArray(handovers.data)[0] ?? null, [handovers.data]);
  const latestProofPackage = useMemo(() => asArray(proofPackages.data)[0] ?? null, [proofPackages.data]);

  const refreshAll = () => bumpRefreshKey();

  const decideRecommendation = (action: "accept" | "reject") => {
    if (!latestRecommendation?.id) return;
    Alert.alert(
      action === "accept" ? "Accept recommendation?" : "Reject recommendation?",
      "This changes the live assignment workflow. The server will re-check permissions and tenant scope.",
      [
        { text: "Cancel", style: "cancel" },
        {
          text: action === "accept" ? "Accept" : "Reject",
          style: action === "reject" ? "destructive" : "default",
          onPress: () => void (async () => {
            try {
              if (action === "accept") await api.acceptSmartAssignment(latestRecommendation.id as number | string);
              else await api.rejectSmartAssignment(latestRecommendation.id as number | string);
              refreshAll();
            } catch (error) {
              Alert.alert("Assignment action failed", error instanceof Error ? error.message : "The server rejected the action.");
            }
          })(),
        },
      ],
    );
  };

  return (
    <Screen>
      <HeroPanel tone="violet">
        <SectionHeader
          eyebrow="Operational workflow"
          title={selectedJobId ? `Work item #${selectedJobId}` : "Smart assignment to proof"}
          description="A mobile view of the selected job’s operational spine. Every read and mutation stays permission-gated."
          right={<Pill label={selectedJobId ? "Loaded" : "Select work"} tone={selectedJobId ? "teal" : "amber"} />}
        />
        <Row>
          <Pill label={canReadRecommendations ? "Assignment readable" : "Assignment hidden"} tone={canReadRecommendations ? "teal" : "amber"} />
          <Pill label={hasPermission("operations.proof.submit") ? "Proof submit enabled" : "Proof submit read-only"} tone={hasPermission("operations.proof.submit") ? "green" : "amber"} />
        </Row>
        <ActionButton label="Refresh workflow data" onPress={refreshAll} variant="secondary" />
      </HeroPanel>

      {selectedJobId == null ? (
        <EmptyState title="No job loaded" body="Return to the dashboard and select a live job. OpsTrax does not allow free-form database ID entry in the mobile workflow." />
      ) : (
        <>
          <Panel variant="elevated" tone="violet">
            <SectionHeader
              eyebrow="Smart assignment"
              title="Recommendation and acceptance state"
              description="AI remains recommendation-only. Accept or reject is available only when the backend grants the action."
            />
            {!canReadRecommendations ? (
              <EmptyState title="Recommendations not available" body="This authenticated session does not grant smart-assignment read access." />
            ) : recommendations.loading ? (
              <LoadingState label="Loading recommendations..." />
            ) : recommendations.error ? (
              <ErrorState title="Recommendation error" body={recommendations.error} onRetry={recommendations.refresh} />
            ) : latestRecommendation ? (
              <View style={{ gap: 10 }}>
                <Row>
                  <View style={{ flex: 1 }}><Field label="Recommendation score" value={textOf(latestRecommendation.score ?? latestRecommendation.recommendation_score)} /></View>
                  <View style={{ flex: 1 }}><Field label="Confidence" value={textOf(latestRecommendation.confidence ?? latestRecommendation.confidence_score)} /></View>
                </Row>
                <Field label="Risk level" value={textOf(latestRecommendation.risk_level ?? latestRecommendation.risk)} />
                <Field label="Status" value={textOf(latestRecommendation.status)} />
                <Field label="Recommended driver" value={textOf(latestRecommendation.driverName ?? latestRecommendation.recommended_driver_name ?? latestRecommendation.recommendedDriverId)} />
                <Field label="Recommended vehicle" value={textOf(latestRecommendation.vehicleName ?? latestRecommendation.recommended_vehicle_name ?? latestRecommendation.recommendedVehicleId)} />
                <Field label="Key reasons" value={textOf(latestRecommendation.reasons ?? latestRecommendation.reasoning ?? latestRecommendation.key_reasons)} />
                <Field label="Missing data" value={textOf(latestRecommendation.constraints ?? latestRecommendation.missing_data ?? latestRecommendation.blockers)} />
              </View>
            ) : (
              <EmptyState title="No recommendation yet" body="The backend has not produced a recommendation for this job yet." />
            )}
            {hasPermission("dispatch.smart_assign.accept") || hasPermission("dispatch.smart_assign.reject") ? (
              <Row>
                {hasPermission("dispatch.smart_assign.accept") ? <ActionButton label={latestRecommendation ? "Accept recommendation" : "Accept unavailable"} onPress={() => decideRecommendation("accept")} disabled={!latestRecommendation} /> : null}
                {hasPermission("dispatch.smart_assign.reject") ? <ActionButton label={latestRecommendation ? "Reject recommendation" : "Reject unavailable"} onPress={() => decideRecommendation("reject")} disabled={!latestRecommendation} variant="danger" /> : null}
              </Row>
            ) : null}
          </Panel>

          <Panel variant="elevated" tone="amber">
            <SectionHeader eyebrow="Site access" title="Gate pass, NOC, and access controls" description="Access blockers stay visible before validation or proof completion." />
            {!canReadSiteAccess ? (
              <EmptyState title="Site access not available" body="This authenticated session does not grant site-access read access." />
            ) : siteAccess.loading ? (
              <LoadingState label="Loading site access..." />
            ) : siteAccess.error ? (
              <ErrorState title="Site access error" body={siteAccess.error} onRetry={siteAccess.refresh} />
            ) : latestSiteAccess ? (
              <View style={{ gap: 10 }}>
                <Field label="Requirement type" value={textOf(latestSiteAccess.requirement_type ?? latestSiteAccess.requirementType)} />
                <Field label="Status" value={textOf(latestSiteAccess.status)} />
                <Field label="Required before" value={textOf(latestSiteAccess.required_before ?? latestSiteAccess.requiredBefore)} />
                <Field label="Instructions / contact" value={textOf(latestSiteAccess.instructions ?? latestSiteAccess.contact)} />
                <Field label="Verification" value={textOf(latestSiteAccess.verified_status ?? latestSiteAccess.verifiedStatus)} />
              </View>
            ) : (
              <EmptyState title="No site access record" body="The job has not yet produced a site access record." />
            )}
          </Panel>

          <Panel variant="elevated" tone="blue">
            <SectionHeader eyebrow="Pickup authorization" title="Third-party handoff control" description="Pickup authorization remains explicit and tenant-scoped." />
            {!canReadPickup ? (
              <EmptyState title="Pickup authorization not available" body="This authenticated session does not grant pickup-authorization read access." />
            ) : pickupAuthorizations.loading ? (
              <LoadingState label="Loading pickup authorizations..." />
            ) : pickupAuthorizations.error ? (
              <ErrorState title="Pickup authorization error" body={pickupAuthorizations.error} onRetry={pickupAuthorizations.refresh} />
            ) : latestPickupAuthorization ? (
              <View style={{ gap: 10 }}>
                <Field label="Authorization number" value={textOf(latestPickupAuthorization.authorization_no ?? latestPickupAuthorization.authorizationNumber)} />
                <Field label="Third-party name" value={textOf(latestPickupAuthorization.third_party_name ?? latestPickupAuthorization.thirdPartyName)} />
                <Field label="Authorized person" value={textOf(latestPickupAuthorization.authorized_person ?? latestPickupAuthorization.authorizedPerson)} />
                <Field label="Validity window" value={textOf(latestPickupAuthorization.valid_from ?? latestPickupAuthorization.validity_window)} />
                <Field label="Verification status" value={textOf(latestPickupAuthorization.verification_status ?? latestPickupAuthorization.status)} />
              </View>
            ) : (
              <EmptyState title="No pickup authorization" body="No third-party pickup authorization is stored yet for this job." />
            )}
          </Panel>

          <Panel variant="elevated" tone="teal">
            <SectionHeader eyebrow="Warehouse handover" title="Inbound / outbound handover state" description="Warehouse completion is visible here without introducing a full warehouse portal." />
            {!canReadHandovers ? (
              <EmptyState title="Warehouse handover not available" body="This authenticated session does not grant warehouse-handover read access." />
            ) : handovers.loading ? (
              <LoadingState label="Loading handovers..." />
            ) : handovers.error ? (
              <ErrorState title="Warehouse handover error" body={handovers.error} onRetry={handovers.refresh} />
            ) : latestHandover ? (
              <View style={{ gap: 10 }}>
                <Field label="Warehouse name" value={textOf(latestHandover.warehouse_name ?? latestHandover.warehouseName)} />
                <Field label="Handover type" value={textOf(latestHandover.handover_type ?? latestHandover.handoverType)} />
                <Field label="Scheduled time" value={textOf(latestHandover.scheduled_time ?? latestHandover.scheduledTime)} />
                <Field label="Status" value={textOf(latestHandover.status)} />
                <Field label="Completed time" value={textOf(latestHandover.completed_at ?? latestHandover.completedTime)} />
              </View>
            ) : (
              <EmptyState title="No warehouse handover" body="The backend has not created a warehouse handover row for this job yet." />
            )}
          </Panel>

          <Panel variant="elevated" tone="green">
            <SectionHeader eyebrow="Proof package" title="POD / proof of delivery" description="The mobile shell shows proof state without auto-validating or issuing anything." />
            {!canReadProof ? (
              <EmptyState title="Proof package not available" body="This authenticated session does not grant proof-package read access." />
            ) : proofPackages.loading ? (
              <LoadingState label="Loading proof packages..." />
            ) : proofPackages.error ? (
              <ErrorState title="Proof package error" body={proofPackages.error} onRetry={proofPackages.refresh} />
            ) : latestProofPackage ? (
              <View style={{ gap: 10 }}>
                <Field label="Proof type" value={textOf(latestProofPackage.proof_type ?? latestProofPackage.proofType)} />
                <Field label="Status" value={textOf(latestProofPackage.status)} />
                <Field label="Validation status" value={textOf(latestProofPackage.validation_status ?? latestProofPackage.validationStatus)} />
                <Field label="Receiver" value={textOf(latestProofPackage.receiver_name ?? latestProofPackage.receiverName)} />
                <Field label="Completed by" value={textOf(latestProofPackage.completed_by ?? latestProofPackage.completedBy)} />
                <Field label="Completed time" value={textOf(latestProofPackage.completed_at ?? latestProofPackage.completedAt)} />
                <Field label="Geo data" value={textOf(latestProofPackage.geo_data ?? latestProofPackage.geoData)} />
                <Field label="Notes" value={textOf(latestProofPackage.notes)} />
              </View>
            ) : (
              <EmptyState title="No proof package" body="The job has not produced a proof package yet." />
            )}
          </Panel>

          <Panel variant="quiet" tone="blue">
            <SectionHeader eyebrow="Billing confidence" title="Trust signal for finance" description="This is a confidence preview only. The mobile app does not issue invoices or change finance state." />
            <Field label="Billing confidence" value={textOf(latestProofPackage?.billing_confidence ?? latestProofPackage?.billingConfidence)} />
            <Field label="Open blockers" value={textOf(latestProofPackage?.blockers ?? latestProofPackage?.missing_data)} />
          </Panel>
        </>
      )}
    </Screen>
  );
}
