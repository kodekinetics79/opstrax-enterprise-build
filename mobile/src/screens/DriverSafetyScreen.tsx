import { useMemo, useState } from "react";
import { Alert, Pressable, StyleSheet, Text, View } from "react-native";
import { ActionButton, EmptyState, ErrorState, Field, Input, LoadingState, Panel, Pill, Row, Screen, SectionHeader, colors } from "@/components/ui";
import { useAsyncResource } from "@/hooks/useAsyncResource";
import { useSession } from "@/auth/SessionProvider";
import type { JsonRecord } from "@/types";

const DvirDriverAttestation = "I certify that this DVIR is true and correct and that I completed this inspection.";

type ChecklistResult = "pass" | "fail" | "na";
type ChecklistAnswer = { result: ChecklistResult; severity: string; notes: string };

function recordOf(value: unknown): JsonRecord {
  return value && typeof value === "object" && !Array.isArray(value) ? value as JsonRecord : {};
}

function arrayOf(value: unknown): JsonRecord[] {
  return Array.isArray(value) ? value as JsonRecord[] : [];
}

function numberOf(value: unknown) {
  const parsed = Number(value);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
}

function statusLabel(value: string) {
  return value.replace(/_/g, " ").replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function makeIdempotencyKey() {
  return `mobile-dvir-${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`;
}

export function DriverSafetyScreen() {
  const { api, hasPermission } = useSession();
  const [refreshKey, setRefreshKey] = useState(0);
  const [confirmationMethod, setConfirmationMethod] = useState<"unit_suffix" | "vin_suffix">("unit_suffix");
  const [confirmationReference, setConfirmationReference] = useState("");
  const [answers, setAnswers] = useState<Record<string, ChecklistAnswer>>({});
  const [dvirIdempotencyKey, setDvirIdempotencyKey] = useState(makeIdempotencyKey);
  const [submitting, setSubmitting] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const canUseDriverSafety = hasPermission("driver:self");
  const assignmentResource = useAsyncResource(
    async () => canUseDriverSafety ? api.currentDriverAssignment() : null,
    [api, canUseDriverSafety, refreshKey],
  );
  const driverResource = useAsyncResource(
    async () => canUseDriverSafety ? api.driverMe() : null,
    [api, canUseDriverSafety, refreshKey],
  );
  const templatesResource = useAsyncResource(
    async () => canUseDriverSafety ? api.driverDvirTemplates() : [],
    [api, canUseDriverSafety, refreshKey],
  );

  const assignmentPayload = recordOf(assignmentResource.data);
  const assignment = recordOf(assignmentPayload.assignment);
  const driverPayload = recordOf(driverResource.data);
  const driver = recordOf(driverPayload.driver);
  const currentAssignment = recordOf(driverPayload.currentAssignment);
  const vehicleBlocking = recordOf(driverPayload.vehicleBlocking);
  const assignmentId = numberOf(assignment.id ?? currentAssignment.id);
  const vehicleId = numberOf(currentAssignment.vehicleId ?? driver.vehicleId);
  const tripId = numberOf(currentAssignment.tripId);
  const driverId = assignmentPayload.driverId;
  const status = String(assignment.assignmentStatus ?? currentAssignment.assignmentStatus ?? "").toLowerCase();
  const vehicleConfirmed = assignment.vehicleConfirmedAt != null &&
    assignment.vehicleConfirmedByDriverId != null &&
    String(assignment.vehicleConfirmedByDriverId) === String(driverId);
  const safePretripRecorded = Boolean(assignment.latestPretripSafeToOperate ?? currentAssignment.latestPretripSafeToOperate);
  const safePretripReady = safePretripRecorded &&
    String(assignment.latestPretripDriverSignatureStatus ?? currentAssignment.latestPretripDriverSignatureStatus ?? "").toLowerCase() === "signed";
  const nextStatuses = arrayOf(assignmentPayload.driverNextStatuses).map(String);
  const template = useMemo(() => {
    const templates = arrayOf(templatesResource.data);
    return templates.find((item) => ["pre_trip", "pre-trip"].includes(String(item.inspectionType ?? "").toLowerCase())) ?? null;
  }, [templatesResource.data]);
  const templateId = numberOf(template?.id);
  const checklistItems = useMemo(() => arrayOf(template?.items), [template]);
  const unitSuffixLength = Math.max(1, Math.min(4, Number(assignment.vehicleUnitSuffixLength) || 4));
  const confirmationLength = confirmationMethod === "vin_suffix" ? 6 : unitSuffixLength;

  const refresh = () => setRefreshKey((value) => value + 1);
  const runAction = async (name: string, action: () => Promise<unknown>) => {
    setSubmitting(name);
    setActionError(null);
    try {
      await action();
      refresh();
    } catch (error) {
      setActionError(error instanceof Error ? error.message : "The server rejected the action.");
    } finally {
      setSubmitting(null);
    }
  };

  const confirmVehicle = () => {
    if (!assignmentId) return;
    const reference = confirmationReference.trim().toUpperCase();
    Alert.alert("Confirm assigned vehicle?", "The server will compare this suffix with the exact vehicle on your active assignment.", [
      { text: "Cancel", style: "cancel" },
      {
        text: "Verify",
        onPress: () => void runAction("vehicle", async () => {
          await api.confirmDriverVehicle(assignmentId, confirmationMethod, reference);
          setConfirmationReference("");
        }),
      },
    ]);
  };

  const setChecklistResult = (key: string, result: ChecklistResult) => {
    setAnswers((current) => ({
      ...current,
      [key]: { result, severity: result === "fail" ? "" : "minor", notes: current[key]?.notes ?? "" },
    }));
  };

  const allRequiredAnswered = checklistItems.length > 0 && checklistItems.every((item, index) => {
    const answer = answers[String(item.id ?? index)];
    return Boolean(answer) && (answer.result !== "fail" || Boolean(answer.severity));
  });
  const hasFailedItem = Object.values(answers).some((answer) => answer.result === "fail");

  const submitPretrip = () => {
    if (!assignmentId || !vehicleId || !templateId || !allRequiredAnswered) return;
    const checklist = checklistItems.map((item, index) => {
      const answer = answers[String(item.id ?? index)];
      return {
        checklistItemId: numberOf(item.id),
        category: String(item.itemCategory ?? "general"),
        itemName: String(item.itemName ?? "Inspection item"),
        result: answer?.result ?? "na",
        severity: answer?.severity ?? "minor",
        notes: answer?.notes || undefined,
      };
    });
    Alert.alert(
      hasFailedItem ? "Submit DVIR with defects?" : "Sign and submit pre-trip DVIR?",
      hasFailedItem
        ? "A failed item will be persisted and may place the vehicle out of service. Departure remains blocked until the server clears every gate."
        : DvirDriverAttestation,
      [
        { text: "Cancel", style: "cancel" },
        {
          text: "Accept attestation and submit",
          onPress: () => void runAction("dvir", () => api.submitDriverDvir({
            vehicleId,
            tripId: tripId ?? undefined,
            templateId,
            inspectionType: "pre_trip",
            checklistItems: checklist,
            attestationAccepted: true,
            attestation: DvirDriverAttestation,
          }, dvirIdempotencyKey).then((result) => {
            setAnswers({});
            setDvirIdempotencyKey(makeIdempotencyKey());
            return result;
          })),
        },
      ],
    );
  };

  const updateStatus = (nextStatus: string) => {
    if (!assignmentId) return;
    const departure = nextStatus === "en_route_pickup";
    Alert.alert(
      departure ? "Begin route?" : `Update status to ${statusLabel(nextStatus)}?`,
      departure
        ? "The server will atomically re-check the exact vehicle confirmation, latest signed safe-to-operate DVIR, out-of-service state, and assignment ownership."
        : "The server will validate the assignment transition and your driver identity.",
      [
        { text: "Cancel", style: "cancel" },
        { text: "Continue", onPress: () => void runAction("status", () => api.updateDriverAssignmentStatus(assignmentId, nextStatus)) },
      ],
    );
  };

  if (!canUseDriverSafety) {
    return <Screen><EmptyState title="Driver safety unavailable" body="This session does not grant driver:self access." /></Screen>;
  }
  if (assignmentResource.loading || driverResource.loading || templatesResource.loading) {
    return <Screen><LoadingState label="Loading driver safety state..." /></Screen>;
  }
  const loadError = assignmentResource.error || driverResource.error || templatesResource.error;
  if (loadError) {
    return <Screen><ErrorState title="Driver safety unavailable" body={loadError} onRetry={refresh} /></Screen>;
  }
  if (!assignmentId) {
    return <Screen><EmptyState title="No active assignment" body="No live dispatch assignment is available for this authenticated driver." /></Screen>;
  }

  const downstreamStatuses = nextStatuses.filter((next) => !["exception", "cancelled"].includes(next));

  return (
    <Screen>
      <Panel>
        <SectionHeader eyebrow="Authenticated driver" title={String(assignment.shipmentNumber ?? currentAssignment.shipmentNumber ?? "Current assignment")} description="Assignment, driver, and vehicle identity come from the authenticated backend session." right={<Pill label={statusLabel(status || "unknown")} tone="teal" />} />
        <Field label="Assigned vehicle" value={String(assignment.vehicleCode ?? currentAssignment.vehicleCode ?? driver.vehicleCode ?? "Unavailable")} />
        <Field label="Pickup" value={String(assignment.pickupAddress ?? currentAssignment.pickupAddress ?? "Not supplied")} />
        <Field label="Delivery" value={String(assignment.dropoffAddress ?? currentAssignment.dropoffAddress ?? "Not supplied")} />
        <ActionButton label="Refresh safety state" onPress={refresh} variant="secondary" />
      </Panel>

      {actionError ? <ErrorState title="Safety action rejected" body={actionError} onRetry={refresh} /> : null}

      {status === "assigned" ? (
        <Panel>
          <SectionHeader eyebrow="Step 1" title="Accept assignment" description="Acceptance is recorded by the server before vehicle confirmation is allowed." />
          <ActionButton label={submitting === "accept" ? "Accepting..." : "Accept assignment"} disabled={submitting !== null} onPress={() => void runAction("accept", () => api.acceptDriverAssignment(assignmentId))} />
        </Panel>
      ) : null}

      {status === "accepted" ? (
        <Panel>
          <SectionHeader eyebrow="Step 2" title="Confirm the exact vehicle" description="Use the suffix displayed on the physical assigned unit or its governed VIN. No vehicle ID is entered or trusted." right={<Pill label={vehicleConfirmed ? "Verified" : "Required"} tone={vehicleConfirmed ? "green" : "amber"} />} />
          <Row>
            <ActionButton label="Unit suffix" variant={confirmationMethod === "unit_suffix" ? "primary" : "secondary"} onPress={() => setConfirmationMethod("unit_suffix")} />
            <ActionButton label="VIN suffix" variant={confirmationMethod === "vin_suffix" ? "primary" : "secondary"} onPress={() => setConfirmationMethod("vin_suffix")} />
          </Row>
          {!vehicleConfirmed ? (
            <>
              <Input label={confirmationMethod === "vin_suffix" ? "Final 6 VIN characters" : `Final ${unitSuffixLength} unit-number character${unitSuffixLength === 1 ? "" : "s"}`} value={confirmationReference} onChangeText={(value) => setConfirmationReference(value.toUpperCase().replace(/[^A-Z0-9-]/g, "").slice(0, 16))} autoCapitalize="characters" autoComplete="off" />
              <ActionButton label={submitting === "vehicle" ? "Verifying..." : "Verify assigned vehicle"} disabled={submitting !== null || confirmationReference.trim().length !== confirmationLength} onPress={confirmVehicle} />
            </>
          ) : <Text style={styles.successText}>The authenticated driver has verified the vehicle on this assignment.</Text>}
        </Panel>
      ) : null}

      {status === "accepted" ? (
        <Panel>
          <SectionHeader eyebrow="Step 3" title="Complete signed pre-trip DVIR" description="Every required persisted checklist item must be answered. Submission includes the exact driver attestation; the backend derives driver identity and decides safe-to-operate state." right={<Pill label={safePretripReady ? "Signed safe" : "Required"} tone={safePretripReady ? "green" : "amber"} />} />
          {Boolean(vehicleBlocking.blocked) ? <ErrorState title="Vehicle departure blocked" body={String(vehicleBlocking.reason ?? "The vehicle has an unresolved safety block.")} /> : null}
          {safePretripReady ? (
            <Text style={styles.successText}>A current signed safe-to-operate pre-trip inspection is recorded for this assignment.</Text>
          ) : !template ? (
            <EmptyState title="No pre-trip template" body="A fleet administrator must publish an active pre-trip DVIR template before inspection can proceed." />
          ) : checklistItems.length === 0 ? (
            <EmptyState title="Checklist unavailable" body="The active pre-trip template has no persisted checklist items. Departure remains blocked." />
          ) : (
            <View style={{ gap: 12 }}>
              <Field label="Checklist" value={String(template.templateName ?? "Pre-trip inspection")} />
              {checklistItems.map((item, index) => {
                const key = String(item.id ?? index);
                const answer = answers[key];
                return (
                  <View key={key} style={styles.checklistItem}>
                    <Text style={styles.itemCategory}>{String(item.itemCategory ?? "General")}</Text>
                    <Text style={styles.itemName}>{String(item.itemName ?? "Inspection item")}</Text>
                    <Row>
                      {(["pass", "fail", ...(Boolean(item.isRequired) ? [] : ["na"])] as ChecklistResult[]).map((result) => (
                        <Pressable key={result} accessibilityRole="button" accessibilityState={{ selected: answer?.result === result }} accessibilityLabel={`${String(item.itemName ?? "Inspection item")}: ${result}`} onPress={() => setChecklistResult(key, result)} style={[styles.resultButton, answer?.result === result && (result === "fail" ? styles.resultFail : styles.resultSelected)]}>
                          <Text style={styles.resultText}>{result.toUpperCase()}</Text>
                        </Pressable>
                      ))}
                    </Row>
                    {answer?.result === "fail" ? (
                      <View style={{ gap: 8 }}>
                        <Text style={styles.itemCategory}>Defect severity required</Text>
                        <Row>
                          {(["minor", "major", "critical"] as const).map((severity) => (
                            <Pressable key={severity} accessibilityRole="button" accessibilityState={{ selected: answer.severity === severity }} accessibilityLabel={`${String(item.itemName ?? "Inspection item")}: ${severity} defect`} onPress={() => setAnswers((current) => ({ ...current, [key]: { ...current[key], severity } }))} style={[styles.resultButton, answer.severity === severity && (severity === "critical" ? styles.resultFail : styles.resultSelected)]}>
                              <Text style={styles.resultText}>{severity.toUpperCase()}</Text>
                            </Pressable>
                          ))}
                        </Row>
                      </View>
                    ) : null}
                  </View>
                );
              })}
              <Text style={styles.attestation}>{DvirDriverAttestation}</Text>
              <ActionButton label={submitting === "dvir" ? "Submitting signed DVIR..." : "Accept attestation and submit DVIR"} disabled={submitting !== null || !allRequiredAnswered} onPress={submitPretrip} />
            </View>
          )}
        </Panel>
      ) : null}

      {status !== "assigned" ? (
        <Panel>
          <SectionHeader eyebrow="Status gates" title="Advance assignment" description="Only server-provided transitions are shown. Departure is disabled until local evidence is present and remains subject to the backend's atomic safety checks." />
          {downstreamStatuses.length === 0 ? <EmptyState title="No driver transition available" body="Refresh after dispatch or safety state changes." /> : null}
          {downstreamStatuses.map((nextStatus) => {
            const departure = nextStatus === "en_route_pickup";
            const locallyReady = !departure || (vehicleConfirmed && safePretripReady && !Boolean(vehicleBlocking.blocked));
            return (
              <ActionButton key={nextStatus} label={departure ? "Begin route to pickup" : `Mark ${statusLabel(nextStatus)}`} disabled={submitting !== null || !locallyReady} onPress={() => updateStatus(nextStatus)} />
            );
          })}
          {!vehicleConfirmed && nextStatuses.includes("en_route_pickup") ? <Text style={styles.blockerText}>Confirm the exact assigned vehicle before departure.</Text> : null}
          {vehicleConfirmed && !safePretripReady && nextStatuses.includes("en_route_pickup") ? <Text style={styles.blockerText}>Submit a signed safe-to-operate pre-trip DVIR before departure.</Text> : null}
        </Panel>
      ) : null}
    </Screen>
  );
}

const styles = StyleSheet.create({
  successText: { color: colors.green, fontSize: 13, lineHeight: 19, fontWeight: "700" },
  blockerText: { color: colors.amber, fontSize: 13, lineHeight: 19, fontWeight: "700" },
  attestation: { color: colors.text, fontSize: 13, lineHeight: 19, padding: 12, borderRadius: 14, borderWidth: 1, borderColor: colors.borderStrong },
  checklistItem: { gap: 8, padding: 12, borderRadius: 16, borderWidth: 1, borderColor: colors.border, backgroundColor: colors.panelAlt },
  itemCategory: { color: colors.subtle, fontSize: 10, fontWeight: "800", letterSpacing: 1.2, textTransform: "uppercase" },
  itemName: { color: colors.text, fontSize: 14, fontWeight: "700" },
  resultButton: { minWidth: 64, alignItems: "center", paddingHorizontal: 12, paddingVertical: 9, borderRadius: 12, borderWidth: 1, borderColor: colors.borderStrong },
  resultSelected: { backgroundColor: colors.green + "33", borderColor: colors.green },
  resultFail: { backgroundColor: colors.red + "33", borderColor: colors.red },
  resultText: { color: colors.text, fontSize: 11, fontWeight: "800" },
});
