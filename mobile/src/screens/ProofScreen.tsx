import { Alert, Text, View } from "react-native";
import { ActionButton, EmptyState, ErrorState, Field, LoadingState, Panel, Row, Screen, SectionHeader } from "@/components/ui";
import { useSession } from "@/auth/SessionProvider";
import { useWorkflow } from "@/workflow/WorkflowContext";
import { useAsyncResource } from "@/hooks/useAsyncResource";
import type { JsonRecord } from "@/types";

function asArray(value: unknown): JsonRecord[] {
  if (Array.isArray(value)) return value as JsonRecord[];
  if (value && typeof value === "object" && Array.isArray((value as Record<string, unknown>).items)) {
    return (value as { items: JsonRecord[] }).items;
  }
  return [];
}

function textOf(value: unknown) {
  return value === null || value === undefined || value === "" ? "No data yet" : String(value);
}

export function ProofScreen() {
  const { api, hasPermission } = useSession();
  const { selectedJobId, bumpRefreshKey, refreshKey } = useWorkflow();
  const canReadProof = ["operations.proof.read", "operations.proof.create", "operations.proof.update", "operations.proof.submit", "operations.proof.validate"].some(hasPermission);
  const canReadArtifacts = ["operations.proof_artifact.read", "operations.proof_artifact.create", "operations.proof.create"].some(hasPermission) || canReadProof;

  const proofPackages = useAsyncResource(async () => (selectedJobId && canReadProof ? api.proofPackages(selectedJobId) : null), [api, selectedJobId, canReadProof, refreshKey]);
  const latestProof = asArray(proofPackages.data)[0] ?? null;

  const proofArtifacts = useAsyncResource(async () => (latestProof?.id && canReadArtifacts ? api.proofArtifacts(latestProof.id as number | string) : null), [api, latestProof?.id, canReadArtifacts, refreshKey]);
  const billingConfidence = useAsyncResource(async () => (latestProof?.id && canReadProof ? api.billingConfidence(latestProof.id as number | string) : null), [api, latestProof?.id, canReadProof, refreshKey]);
  const artifacts = asArray(proofArtifacts.data);
  const proofStatus = String(latestProof?.status ?? "").toLowerCase();
  const canSubmitState = Boolean(latestProof?.id) && ["draft", "rejected"].includes(proofStatus) && artifacts.length > 0;
  const canValidateState = Boolean(latestProof?.id) && proofStatus === "submitted";

  const runProofAction = (action: "submit" | "validate") => {
    if (!latestProof?.id) return;
    Alert.alert(
      action === "submit" ? "Submit proof package?" : "Validate proof package?",
      "The server will enforce tenant scope, assignment scope, evidence, and allowed status transitions.",
      [
        { text: "Cancel", style: "cancel" },
        {
          text: action === "submit" ? "Submit" : "Validate",
          onPress: () => void (async () => {
            try {
              if (action === "submit") await api.submitProofPackage(latestProof.id as number | string);
              else await api.validateProofPackage(latestProof.id as number | string);
              bumpRefreshKey();
            } catch (error) {
              Alert.alert("Proof action failed", error instanceof Error ? error.message : "The server rejected the action.");
            }
          })(),
        },
      ],
    );
  };

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
            {!canReadProof ? (
              <EmptyState title="Proof not available" body="This authenticated session does not grant proof-package access." />
            ) : proofPackages.loading ? (
              <LoadingState label="Loading proof packages..." />
            ) : proofPackages.error ? (
              <ErrorState title="Proof packages unavailable" body={proofPackages.error} onRetry={proofPackages.refresh} />
            ) : latestProof ? (
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
              {hasPermission("operations.proof.submit") ? <ActionButton label={canSubmitState ? "Submit" : !latestProof ? "Submit (no package)" : artifacts.length === 0 ? "Submit (evidence required)" : "Submit (draft or rejected only)"} onPress={() => runProofAction("submit")} disabled={!canSubmitState} /> : null}
              {hasPermission("operations.proof.validate") ? <ActionButton label={canValidateState ? "Validate" : !latestProof ? "Validate (no package)" : "Validate (submitted only)"} onPress={() => runProofAction("validate")} disabled={!canValidateState} variant="secondary" /> : null}
            </Row>
            <Text style={{ color: "white" }}>AI and the mobile client never execute business actions automatically.</Text>
          </Panel>

          <Panel>
            <SectionHeader eyebrow="Evidence artifacts" title="Uploaded or captured proof metadata" description="The app does not fake file uploads. If the file service is not ready, the gap stays visible." />
            {!canReadArtifacts ? (
              <EmptyState title="Artifacts not available" body="This authenticated session does not grant proof-artifact access." />
            ) : proofArtifacts.loading ? (
              <LoadingState label="Loading proof artifacts..." />
            ) : proofArtifacts.error ? (
              <ErrorState title="Artifacts unavailable" body={proofArtifacts.error} onRetry={proofArtifacts.refresh} />
            ) : artifacts.length === 0 ? (
              <EmptyState title="No evidence artifacts" body="No proof artifacts were returned for the selected proof package." />
            ) : (
              artifacts.map((artifact, index) => (
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
            {!canReadProof ? (
              <EmptyState title="Billing confidence not available" body="Proof read access is required for this signal." />
            ) : billingConfidence.loading ? (
              <LoadingState label="Loading billing confidence..." />
            ) : billingConfidence.error ? (
              <ErrorState title="Billing confidence unavailable" body={billingConfidence.error} onRetry={billingConfidence.refresh} />
            ) : billingConfidence.data ? (
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
