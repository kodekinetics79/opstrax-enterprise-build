import { useState } from "react";
import { Alert, Linking, View } from "react-native";
import * as Haptics from "expo-haptics";
import { ActionButton, EmptyState, ErrorState, Field, Input, LoadingState, Panel, Pill, Row, Screen, SectionHeader, toneForStatus } from "@/components/ui";
import { useSession } from "@/auth/SessionProvider";
import { useAsyncResource } from "@/hooks/useAsyncResource";
import { textOf, titleCase } from "@/data/records";

const EXCEPTION_TYPES = ["route_blocked", "late_pickup", "late_delivery", "vehicle_breakdown", "customer_hold", "safety_hold", "general"];

export function DriverTripScreen() {
  const { api } = useSession();
  const current = useAsyncResource(() => api.driverCurrentAssignment(), [api]);
  const [showException, setShowException] = useState(false);
  const [exceptionType, setExceptionType] = useState("route_blocked");
  const [exceptionNotes, setExceptionNotes] = useState("");
  const [busy, setBusy] = useState(false);
  const assignment = current.data?.assignment;

  const openMaps = async (address?: string) => {
    if (!address) return;
    const url = `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(address)}`;
    if (await Linking.canOpenURL(url)) await Linking.openURL(url);
  };

  const transition = (status: string) => {
    if (!assignment?.id) return;
    Alert.alert(`Mark ${titleCase(status)}?`, "This updates the live dispatch board and cannot be undone from the mobile app.", [
      { text: "Cancel", style: "cancel" },
      {
        text: "Update",
        onPress: () => void (async () => {
          setBusy(true);
          try {
            await api.updateDriverAssignmentStatus(assignment.id, status);
            await Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
            current.refresh();
          } catch (error) {
            Alert.alert("Status update failed", error instanceof Error ? error.message : "The server rejected the transition.");
          } finally {
            setBusy(false);
          }
        })(),
      },
    ]);
  };

  const reportException = async () => {
    if (!assignment?.id) return;
    setBusy(true);
    try {
      await api.reportDriverException(assignment.id, {
        exceptionType,
        severity: exceptionType.includes("safety") || exceptionType.includes("breakdown") ? "High" : "Medium",
        title: titleCase(exceptionType),
        notes: exceptionNotes.trim(),
      });
      await Haptics.notificationAsync(Haptics.NotificationFeedbackType.Warning);
      setShowException(false);
      setExceptionNotes("");
      current.refresh();
    } catch (error) {
      Alert.alert("Exception report failed", error instanceof Error ? error.message : "The server rejected the report.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <Screen>
      <Panel>
        <SectionHeader eyebrow="Live trip" title={textOf(assignment?.shipmentNumber, "Trip")} description="Only assignment transitions allowed by the backend state machine are shown." right={assignment ? <Pill label={titleCase(assignment.assignmentStatus)} tone={toneForStatus(assignment.assignmentStatus)} /> : undefined} />
      </Panel>

      {current.loading ? <LoadingState label="Loading trip…" /> : null}
      {current.error ? <ErrorState title="Trip unavailable" body={current.error} /> : null}
      {!current.loading && !current.error && !assignment ? <EmptyState title="No active trip" body="A dispatch assignment will appear here when it is assigned to your driver identity." /> : null}

      {assignment ? (
        <>
          <Panel>
            <SectionHeader eyebrow="Route" title="Pickup to delivery" description="Open the destination in your device’s maps app. OpsTrax does not fabricate route geometry." />
            <View style={{ gap: 10 }}>
              <Field label="Pickup" value={assignment.pickupAddress} />
              <ActionButton label="Navigate to pickup" onPress={() => void openMaps(assignment.pickupAddress)} variant="secondary" disabled={!assignment.pickupAddress} />
              <Field label="Delivery" value={assignment.dropoffAddress} />
              <ActionButton label="Navigate to delivery" onPress={() => void openMaps(assignment.dropoffAddress)} variant="secondary" disabled={!assignment.dropoffAddress} />
            </View>
          </Panel>

          <Panel>
            <SectionHeader eyebrow="Progress" title="Update live status" description="Delivery completion is never a status button; it requires proof on the Proof tab." />
            {current.data?.driverNextStatuses?.length ? (
              <View style={{ gap: 10 }}>
                {current.data.driverNextStatuses.filter((status) => status !== "exception").map((status) => (
                  <ActionButton key={status} label={`Mark ${titleCase(status)}`} onPress={() => transition(status)} disabled={busy} />
                ))}
              </View>
            ) : (
              <EmptyState title="No transition available" body="Refresh after dispatch changes the assignment, or complete the required proof step." />
            )}
          </Panel>

          <Panel>
            <SectionHeader eyebrow="Exception" title="Tell operations immediately" description="Exception reports notify dispatch and fleet management and preserve the prior trip state." />
            {showException ? (
              <View style={{ gap: 12 }}>
                <TextPicker values={EXCEPTION_TYPES} selected={exceptionType} onSelect={setExceptionType} />
                <Input label="What happened?" value={exceptionNotes} onChangeText={setExceptionNotes} placeholder="Describe what happened and what support you need." multiline autoCapitalize="sentences" />
                <Row>
                  <ActionButton label="Cancel" onPress={() => setShowException(false)} variant="ghost" disabled={busy} />
                  <ActionButton label={busy ? "Reporting…" : "Report exception"} onPress={() => void reportException()} variant="danger" disabled={busy || exceptionNotes.trim().length < 3} />
                </Row>
              </View>
            ) : (
              <ActionButton label="Report an exception" onPress={() => setShowException(true)} variant="danger" />
            )}
          </Panel>
        </>
      ) : null}
    </Screen>
  );
}

function TextPicker({ values, selected, onSelect }: { values: string[]; selected: string; onSelect: (value: string) => void }) {
  return (
    <Row>
      {values.map((value) => (
        <ActionButton key={value} label={titleCase(value)} onPress={() => onSelect(value)} variant={selected === value ? "secondary" : "ghost"} />
      ))}
    </Row>
  );
}
