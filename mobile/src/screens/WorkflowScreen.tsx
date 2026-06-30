import { useMemo } from "react";
import { View } from "react-native";
import { ActionButton, EmptyState, Field, LoadingState, Panel, Pill, Row, Screen, SectionHeader } from "@/components/ui";
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

  const recommendations = useAsyncResource(
    async () => (selectedJobId ? api.smartAssignmentRecommendations(selectedJobId) : []),
    [api, selectedJobId, refreshKey],
  );
  const siteAccess = useAsyncResource(async () => (selectedJobId ? api.siteAccess(selectedJobId) : []), [api, selectedJobId, refreshKey]);
  const pickupAuthorizations = useAsyncResource(async () => (selectedJobId ? api.pickupAuthorizations(selectedJobId) : []), [api, selectedJobId, refreshKey]);
  const handovers = useAsyncResource(async () => (selectedJobId ? api.warehouseHandovers(selectedJobId) : []), [api, selectedJobId, refreshKey]);
  const proofPackages = useAsyncResource(async () => (selectedJobId ? api.proofPackages(selectedJobId) : []), [api, selectedJobId, refreshKey]);

  const latestRecommendation = useMemo(() => asArray(recommendations.data)[0] ?? null, [recommendations.data]);
  const latestSiteAccess = useMemo(() => asArray(siteAccess.data)[0] ?? null, [siteAccess.data]);
  const latestPickupAuthorization = useMemo(() => asArray(pickupAuthorizations.data)[0] ?? null, [pickupAuthorizations.data]);
  const latestHandover = useMemo(() => asArray(handovers.data)[0] ?? null, [handovers.data]);
  const latestProofPackage = useMemo(() => asArray(proofPackages.data)[0] ?? null, [proofPackages.data]);

  const refreshAll = () => bumpRefreshKey();

  return (
    <Screen>
      <Panel>
        <SectionHeader eyebrow="Operational workflow" title="Smart assignment to proof" description="This tab shows the live operational spine for the selected job. Every action is permission-gated." />
        <Row>
          <Pill label={hasPermission("dispatch.smart_assign.read") ? "Assignment readable" : "Assignment hidden"} tone={hasPermission("dispatch.smart_assign.read") ? "teal" : "amber"} />
          <Pill label={hasPermission("operations.proof.submit") ? "Proof submit enabled" : "Proof submit read-only"} tone={hasPermission("operations.proof.submit") ? "teal" : "amber"} />
        </Row>
        <ActionButton label="Refresh workflow data" onPress={refreshAll} variant="secondary" />
      </Panel>

      {selectedJobId == null ? (
        <EmptyState title="No job loaded" body="Return to the dashboard and select a live job id. The app will then load the operational workflow from the backend." />
      ) : (
        <>
          <Panel>
            <SectionHeader eyebrow="Smart assignment" title="Recommendation and acceptance state" description="AI remains recommendation-only. Accept/reject is only available when the backend permission exists." />
            {recommendations.loading ? <LoadingState label="Loading recommendations..." /> : recommendations.error ? <EmptyState title="Recommendation error" body={recommendations.error} /> : null}
            {latestRecommendation ? (
              <View style={{ gap: 10 }}>
                <Field label="Recommendation score" value={textOf(latestRecommendation.score ?? latestRecommendation.recommendation_score)} />
                <Field label="Confidence" value={textOf(latestRecommendation.confidence ?? latestRecommendation.confidence_score)} />
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
                {hasPermission("dispatch.smart_assign.accept") ? <ActionButton label="Accept" onPress={() => refreshAll()} /> : null}
                {hasPermission("dispatch.smart_assign.reject") ? <ActionButton label="Reject" onPress={() => refreshAll()} variant="secondary" /> : null}
              </Row>
            ) : null}
          </Panel>

          <Panel>
            <SectionHeader eyebrow="Site access" title="Gate pass, NOC, and access controls" description="This view keeps access blockers visible before validation or proof completion." />
            {siteAccess.loading ? <LoadingState label="Loading site access..." /> : siteAccess.error ? <EmptyState title="Site access error" body={siteAccess.error} /> : null}
            {latestSiteAccess ? (
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

          <Panel>
            <SectionHeader eyebrow="Pickup authorization" title="Third-party handoff control" description="Pickup authorization remains explicit and tenant-scoped." />
            {pickupAuthorizations.loading ? <LoadingState label="Loading pickup authorizations..." /> : pickupAuthorizations.error ? <EmptyState title="Pickup authorization error" body={pickupAuthorizations.error} /> : null}
            {latestPickupAuthorization ? (
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

          <Panel>
            <SectionHeader eyebrow="Warehouse handover" title="Inbound / outbound handover state" description="Warehouse completion is visible here without introducing a full warehouse portal." />
            {handovers.loading ? <LoadingState label="Loading handovers..." /> : handovers.error ? <EmptyState title="Warehouse handover error" body={handovers.error} /> : null}
            {latestHandover ? (
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

          <Panel>
            <SectionHeader eyebrow="Proof package" title="POD / proof of delivery" description="The mobile shell shows proof package state without auto-validating or issuing anything." />
            {proofPackages.loading ? <LoadingState label="Loading proof packages..." /> : proofPackages.error ? <EmptyState title="Proof package error" body={proofPackages.error} /> : null}
            {latestProofPackage ? (
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

          <Panel>
            <SectionHeader eyebrow="Billing confidence" title="Trust signal for finance" description="This is a confidence preview only. The mobile app does not issue invoices or change finance state." />
            <Field label="Billing confidence" value={textOf(latestProofPackage?.billing_confidence ?? latestProofPackage?.billingConfidence)} />
            <Field label="Open blockers" value={textOf(latestProofPackage?.blockers ?? latestProofPackage?.missing_data)} />
          </Panel>
        </>
      )}
    </Screen>
  );
}
