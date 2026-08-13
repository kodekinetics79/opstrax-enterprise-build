import { Alert, View } from "react-native";
import * as Haptics from "expo-haptics";
import { ActionButton, EmptyState, ErrorState, Field, LoadingState, MetricCard, Panel, Pill, Row, Screen, SectionHeader, toneForStatus } from "@/components/ui";
import { useSession } from "@/auth/SessionProvider";
import { useAsyncResource } from "@/hooks/useAsyncResource";
import { textOf, titleCase } from "@/data/records";

export function DriverTodayScreen() {
  const { api, session } = useSession();
  const profile = useAsyncResource(() => api.driverMe(), [api]);
  const current = useAsyncResource(() => api.driverCurrentAssignment(), [api]);
  const assignment = current.data?.assignment;
  const driver = profile.data?.driver;
  const blocked = profile.data?.vehicleBlocking?.blocked === true;

  const accept = () => {
    if (!assignment?.id) return;
    Alert.alert("Accept this assignment?", "Your dispatcher will immediately see that you accepted the load.", [
      { text: "Not now", style: "cancel" },
      {
        text: "Accept",
        onPress: () => void (async () => {
          try {
            await api.acceptDriverAssignment(assignment.id);
            await Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
            current.refresh();
            profile.refresh();
          } catch (error) {
            Alert.alert("Could not accept assignment", error instanceof Error ? error.message : "The server rejected the change.");
          }
        })(),
      },
    ]);
  };

  return (
    <Screen>
      <Panel>
        <SectionHeader
          eyebrow={session?.company.name}
          title={`Good day, ${driver?.fullName ?? session?.user.name ?? "driver"}`}
          description="Your current vehicle, load, compliance blocks, and next action come from the tenant-secured driver record."
          right={<Pill label={textOf(driver?.status, "Driver")} tone={toneForStatus(String(driver?.status ?? ""))} />}
        />
        <Row>
          <MetricCard label="Vehicle" value={textOf(assignment?.vehicleCode ?? driver?.vehicleCode, "Unassigned")} tone={blocked ? "red" : "teal"} />
          <MetricCard label="Drive left" value={profile.data?.hos?.dataAvailable ? `${textOf(profile.data.hos.remainingDriveHours)}h` : "No ELD data"} tone="blue" />
          <MetricCard label="Coaching" value={String(profile.data?.coaching?.pendingCount ?? 0)} tone={(profile.data?.coaching?.pendingCount ?? 0) > 0 ? "amber" : "green"} />
        </Row>
      </Panel>

      {profile.loading || current.loading ? <LoadingState label="Loading your live work…" /> : null}
      {profile.error ? <ErrorState title="Driver profile unavailable" body={profile.error} /> : null}
      {current.error ? <ErrorState title="Assignment unavailable" body={current.error} /> : null}

      {blocked ? (
        <ErrorState title="Vehicle blocked" body={profile.data?.vehicleBlocking?.reason ?? "Do not operate this vehicle until maintenance clears it."} />
      ) : null}

      <Panel>
        <SectionHeader
          eyebrow="Current load"
          title={textOf(assignment?.shipmentNumber, "No active assignment")}
          description={assignment ? textOf(assignment.customerName, "Customer details available in dispatch") : "You are clear. Your next load will appear here when dispatch assigns it."}
          right={assignment ? <Pill label={titleCase(assignment.assignmentStatus)} tone={toneForStatus(assignment.assignmentStatus)} /> : undefined}
        />
        {assignment ? (
          <View style={{ gap: 10 }}>
            <Field label="Pickup" value={assignment.pickupAddress} />
            <Field label="Delivery" value={assignment.dropoffAddress} />
            <Field label="Pickup window" value={assignment.plannedPickupAt} />
            <Field label="Delivery window" value={assignment.plannedDeliveryAt} />
            {String(assignment.assignmentStatus ?? "").toLowerCase() === "assigned" ? (
              <ActionButton label="Accept assignment" onPress={accept} disabled={blocked} />
            ) : null}
          </View>
        ) : (
          <EmptyState title="Nothing assigned right now" body="This is a live empty state—no placeholder load has been created." />
        )}
      </Panel>

      {profile.data?.guidance?.length ? (
        <Panel>
          <SectionHeader eyebrow="Next best action" title="Driver guidance" description="Rule-based guidance from your real assignment, HOS, DVIR, and coaching state." />
          <View style={{ gap: 10 }}>
            {profile.data.guidance.map((item, index) => (
              <Field key={`${item.level}-${index}`} label={titleCase(item.level ?? "Guidance")} value={item.message} />
            ))}
          </View>
        </Panel>
      ) : null}
    </Screen>
  );
}
