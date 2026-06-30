import { Text, View } from "react-native";
import { ActionButton, EmptyState, Field, LoadingState, Panel, Row, Screen, SectionHeader } from "@/components/ui";
import { useSession } from "@/auth/SessionProvider";
import { useWorkflow } from "@/workflow/WorkflowContext";
import { useAsyncResource } from "@/hooks/useAsyncResource";
import type { JsonRecord } from "@/types";

function asArray(value: unknown): JsonRecord[] {
  if (Array.isArray(value)) return value as JsonRecord[];
  return [];
}

function textOf(value: unknown) {
  return value === null || value === undefined || value === "" ? "No data yet" : String(value);
}

export function ProofScreen() {
  const { api, hasPermission } = useSession();
  const { selectedJobId, bumpRefreshKey, refreshKey } = useWorkflow();

  const proofPackages = useAsyncResource(async () => (selectedJobId ? api.proofPackages(selectedJobId) : []), [api, selectedJobId, refreshKey]);
  const latestProof = asArray(proofPackages.data)[0] ?? null;

  const proofArtifacts = useAsyncResource(async () => (latestProof?.id ? api.proofArtifacts(latestProof.id as number | string) : []), [api, latestProof?.id, refreshKey]);
  const billingConfidence = useAsyncResource(async () => (latestProof?.id ? api.billingConfidence(latestProof.id as number | string) : null), [api, latestProof?.id, refreshKey]);

  return (
    <Screen>
      <Panel>
        <SectionHeader eyebrow="Proof center" title="Evidence, submit, validate" description="This screen focuses on proof package life cycle and evidence artifacts." />
        <Row>
          <Text style={{ color: "white" }}>{hasPermission("operations.proof.submit") ? "Submit enabled" : "Read-only proof"}</Text>
        </Row>
        <ActionButton label="Refresh proof data" onPress={bumpRefreshKey} variant="secondary" />
      </Panel>

      {selectedJobId == null ? (
        <EmptyState title="No job loaded" body="Select a job from the dashboard to see proof package and evidence records." />
      ) : (
        <>
          <Panel>
            <SectionHeader eyebrow="Proof package" title="Current package" description="The app renders the package exactly as the backend returned it." />
            {proofPackages.loading ? <LoadingState label="Loading proof packages..." /> : proofPackages.error ? <EmptyState title="Proof packages unavailable" body={proofPackages.error} /> : null}
            {latestProof ? (
              <View style={{ gap: 10 }}>
                <Field label="Proof type" value={textOf(latestProof.proof_type ?? latestProof.proofType)} />
                <Field label="Status" value={textOf(latestProof.status)} />
                <Field label="Validation status" value={textOf(latestProof.validation_status ?? latestProof.validationStatus)} />
                <Field label="Receiver name" value={textOf(latestProof.receiver_name ?? latestProof.receiverName)} />
                <Field label="Receiver phone" value={textOf(latestProof.receiver_phone ?? latestProof.receiverPhone)} />
                <Field label="Geo data" value={textOf(latestProof.geo_data ?? latestProof.geoData)} />
                <Field label="Notes" value={textOf(latestProof.notes)} />
              </View>
            ) : (
              <EmptyState title="No proof package yet" body="The backend has not created a proof package for this job." />
            )}
          </Panel>

          <Panel>
            <SectionHeader eyebrow="Proof actions" title="Submit and validate" description="Action buttons are visible only when the session has the corresponding permission." />
            <Row>
              {hasPermission("operations.proof.submit") ? <ActionButton label="Submit" onPress={bumpRefreshKey} /> : null}
              {hasPermission("operations.proof.validate") ? <ActionButton label="Validate" onPress={bumpRefreshKey} variant="secondary" /> : null}
            </Row>
            <Text style={{ color: "white" }}>AI and the mobile client never execute business actions automatically.</Text>
          </Panel>

          <Panel>
            <SectionHeader eyebrow="Evidence artifacts" title="Uploaded or captured proof metadata" description="The app does not fake file uploads. If the file service is not ready, the gap stays visible." />
            {proofArtifacts.loading ? <LoadingState label="Loading proof artifacts..." /> : proofArtifacts.error ? <EmptyState title="Artifacts unavailable" body={proofArtifacts.error} /> : null}
            {asArray(proofArtifacts.data).length === 0 ? (
              <EmptyState title="No evidence artifacts" body="No proof artifacts were returned for the selected proof package." />
            ) : (
              asArray(proofArtifacts.data).map((artifact, index) => (
                <View key={String(artifact.id ?? index)} style={{ gap: 10 }}>
                  <Field label="Artifact type" value={textOf(artifact.artifact_type ?? artifact.artifactType)} />
                  <Field label="File reference" value={textOf(artifact.file_ref ?? artifact.fileReference)} />
                  <Field label="Captured at" value={textOf(artifact.captured_at ?? artifact.capturedAt)} />
                  <Field label="Uploaded at" value={textOf(artifact.uploaded_at ?? artifact.uploadedAt)} />
                  <Field label="Captured by" value={textOf(artifact.captured_by ?? artifact.capturedBy)} />
                  <Field label="Device id" value={textOf(artifact.device_id ?? artifact.deviceId)} />
                  <Field label="Geo data" value={textOf(artifact.geo_data ?? artifact.geoData)} />
                </View>
              ))
            )}
          </Panel>

          <Panel>
            <SectionHeader eyebrow="Billing confidence" title="Finance trust preview" description="This is a read-only confidence signal. It never issues invoices." />
            {billingConfidence.loading ? <LoadingState label="Loading billing confidence..." /> : billingConfidence.error ? <EmptyState title="Billing confidence unavailable" body={billingConfidence.error} /> : null}
            {billingConfidence.data ? (
              <View style={{ gap: 10 }}>
                <Field label="Confidence score" value={textOf((billingConfidence.data as JsonRecord).score ?? (billingConfidence.data as JsonRecord).confidence)} />
                <Field label="Status" value={textOf((billingConfidence.data as JsonRecord).status)} />
                <Field label="Blockers" value={textOf((billingConfidence.data as JsonRecord).blockers ?? (billingConfidence.data as JsonRecord).missing_data)} />
                <Field label="Next action" value={textOf((billingConfidence.data as JsonRecord).next_action ?? (billingConfidence.data as JsonRecord).nextBestAction)} />
              </View>
            ) : (
              <EmptyState title="No billing confidence" body="Billing confidence is only shown when the backend returns it." />
            )}
          </Panel>
        </>
      )}
    </Screen>
  );
}

