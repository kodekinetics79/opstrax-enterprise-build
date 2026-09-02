import { useMemo, useState } from "react";
import { Alert, Pressable, Text, View } from "react-native";
import * as Haptics from "expo-haptics";
import { ActionButton, EmptyState, ErrorState, Field, Input, LoadingState, MetricCard, Panel, Pill, Row, Screen, SectionHeader, colors } from "@/components/ui";
import { useSession } from "@/auth/SessionProvider";
import { useAsyncResource } from "@/hooks/useAsyncResource";
import { asRecords, numberOf, textOf, titleCase } from "@/data/records";
import type { JsonRecord } from "@/types";

const ATTESTATION = "I certify that this DVIR is true and correct and that I completed this inspection.";

type ChecklistResult = { result: "pass" | "fail"; severity: "minor" | "major" | "critical"; notes: string };

export function DriverComplianceScreen() {
  const { api, session } = useSession();
  const profile = useAsyncResource(() => api.driverMe(), [api]);
  const current = useAsyncResource(() => api.driverCurrentAssignment(), [api]);
  const templates = useAsyncResource(() => api.driverDvirTemplates(), [api]);
  const hos = useAsyncResource(() => api.driverHos(), [api]);
  const coaching = useAsyncResource(() => api.driverCoaching(), [api]);
  const [results, setResults] = useState<Record<string, ChecklistResult>>({});
  const [attested, setAttested] = useState(false);
  const [busy, setBusy] = useState(false);

  const template = asRecords(templates.data)[0];
  const items = useMemo(() => asRecords(template?.items), [template]);
  const assignment = current.data?.assignment;
  const vehicleId = assignment?.vehicleId ?? profile.data?.driver?.vehicleId;
  const vehicleCode = assignment?.vehicleCode ?? profile.data?.driver?.vehicleCode;
  const completed = items.length > 0 && items.every((item, index) => results[String(item.id ?? index)]?.result);

  const toggle = (item: JsonRecord, index: number, result: "pass" | "fail") => {
    const key = String(item.id ?? index);
    setResults((currentResults) => ({
      ...currentResults,
      [key]: {
        result,
        severity: result === "fail" ? currentResults[key]?.severity ?? "major" : "minor",
        notes: currentResults[key]?.notes ?? "",
      },
    }));
  };

  const submitDvir = async () => {
    if (!vehicleId || !profile.data?.driver?.id || !template) return;
    setBusy(true);
    try {
      const idempotencyKey = [
        "mobile-dvir",
        String(session?.company.id ?? session?.company.code),
        String(session?.user.id),
        String(vehicleId),
        new Date().toISOString().slice(0, 10),
        Date.now().toString(36),
      ].join(":").slice(0, 100);
      await api.submitDriverDvir({
        vehicleId,
        driverId: profile.data.driver.id,
        tripId: assignment?.tripId,
        inspectionType: textOf(template.inspectionType ?? "pre_trip"),
        checklistItems: items.map((item, index) => {
          const value = results[String(item.id ?? index)]!;
          return {
            category: textOf(item.itemCategory ?? "general"),
            itemName: textOf(item.itemName ?? item.itemLabel),
            result: value.result,
            severity: value.severity,
            notes: value.notes || undefined,
          };
        }),
        attestationAccepted: true,
        attestation: ATTESTATION,
      }, idempotencyKey);
      await Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      setResults({});
      setAttested(false);
      profile.refresh();
      Alert.alert("DVIR submitted", "Your inspection is recorded. Critical failures automatically block the vehicle.");
    } catch (error) {
      Alert.alert("DVIR submission failed", error instanceof Error ? error.message : "The server rejected the inspection.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <Screen>
      <Panel>
        <SectionHeader eyebrow="Compliance" title="Safe and legal to drive" description="Your HOS, assigned vehicle, inspection checklist, and coaching are identity-scoped by the backend." />
        <Row>
          <MetricCard label="Vehicle" value={textOf(vehicleCode, "Unassigned")} tone={profile.data?.vehicleBlocking?.blocked ? "red" : "teal"} />
          <MetricCard label="Drive hours" value={hos.data?.dataAvailable === false ? "Unavailable" : `${textOf(hos.data?.remainingDriveHours)}h`} tone={numberOf(hos.data?.remainingDriveHours) !== null && numberOf(hos.data?.remainingDriveHours)! < 3 ? "amber" : "blue"} />
          <MetricCard label="Coaching" value={String(coaching.data?.pendingCount ?? 0)} tone={(coaching.data?.pendingCount ?? 0) > 0 ? "amber" : "green"} />
        </Row>
      </Panel>

      {profile.error || current.error ? <ErrorState title="Compliance profile unavailable" body={profile.error ?? current.error ?? "Unknown error"} /> : null}

      <Panel>
        <SectionHeader eyebrow="Hours of service" title={textOf(hos.data?.hosStatus, "ELD status unavailable")} description="HOS is read directly from your paired ELD record." />
        {hos.loading ? <LoadingState label="Loading HOS…" /> : hos.error ? <ErrorState title="HOS unavailable" body={hos.error} /> : (
          <Row>
            <MetricCard label="Drive" value={hos.data?.dataAvailable === false ? "No data" : `${textOf(hos.data?.remainingDriveHours)}h`} tone="teal" />
            <MetricCard label="Shift" value={hos.data?.dataAvailable === false ? "No data" : `${textOf(hos.data?.remainingShiftHours)}h`} tone="blue" />
            <MetricCard label="Cycle" value={hos.data?.dataAvailable === false ? "No data" : `${textOf(hos.data?.remainingCycleHours)}h`} tone="green" />
          </Row>
        )}
      </Panel>

      <Panel>
        <SectionHeader eyebrow="Pre-trip DVIR" title={textOf(template?.templateName, "Inspection checklist")} description="Every item must be marked. Critical failures immediately place the vehicle out of service." right={<Pill label={textOf(vehicleCode, "No vehicle")} tone={vehicleId ? "teal" : "red"} />} />
        {templates.loading ? <LoadingState label="Loading tenant checklist…" /> : templates.error ? <ErrorState title="Checklist unavailable" body={templates.error} /> : null}
        {!templates.loading && items.length === 0 ? <EmptyState title="No active DVIR template" body="Ask the fleet administrator to publish a tenant inspection template." /> : null}
        <View style={{ gap: 10 }}>
          {items.map((item, index) => {
            const key = String(item.id ?? index);
            const value = results[key];
            return (
              <View key={key} style={{ gap: 9, padding: 13, borderRadius: 17, borderWidth: 1, borderColor: value?.result === "fail" ? colors.red + "66" : colors.border, backgroundColor: colors.panelAlt }}>
                <Text style={{ color: colors.text, fontWeight: "800", lineHeight: 20 }}>{textOf(item.itemName ?? item.itemLabel)}</Text>
                <Row>
                  <ActionButton label="Pass" onPress={() => toggle(item, index, "pass")} variant={value?.result === "pass" ? "secondary" : "ghost"} />
                  <ActionButton label="Fail" onPress={() => toggle(item, index, "fail")} variant={value?.result === "fail" ? "danger" : "ghost"} />
                </Row>
                {value?.result === "fail" ? (
                  <>
                    <Row>
                      {(["minor", "major", "critical"] as const).map((severity) => (
                        <ActionButton key={severity} label={titleCase(severity)} onPress={() => setResults((all) => ({ ...all, [key]: { ...all[key], severity } }))} variant={value.severity === severity ? "secondary" : "ghost"} />
                      ))}
                    </Row>
                    <Input label="Defect details" value={value.notes} onChangeText={(notes) => setResults((all) => ({ ...all, [key]: { ...all[key], notes } }))} placeholder="Describe the defect" multiline autoCapitalize="sentences" />
                  </>
                ) : null}
              </View>
            );
          })}
        </View>
        {items.length ? (
          <>
            <Pressable accessibilityRole="checkbox" accessibilityState={{ checked: attested }} onPress={() => setAttested((value) => !value)} style={{ flexDirection: "row", gap: 10, alignItems: "flex-start" }}>
              <View style={{ width: 24, height: 24, borderRadius: 7, borderWidth: 1, borderColor: attested ? colors.teal : colors.borderStrong, backgroundColor: attested ? colors.teal : "transparent", alignItems: "center", justifyContent: "center" }}>
                <Text style={{ color: colors.backgroundDeep, fontWeight: "900" }}>{attested ? "✓" : ""}</Text>
              </View>
              <Text style={{ flex: 1, color: colors.muted, lineHeight: 20 }}>{ATTESTATION}</Text>
            </Pressable>
            <ActionButton label={busy ? "Submitting inspection…" : "Submit DVIR"} onPress={() => void submitDvir()} disabled={busy || !vehicleId || !completed || !attested} />
          </>
        ) : null}
      </Panel>

      <Panel>
        <SectionHeader eyebrow="Coaching" title="Required acknowledgements" description="Only your own coaching tasks are returned." />
        {coaching.loading ? <LoadingState label="Loading coaching…" /> : coaching.error ? <ErrorState title="Coaching unavailable" body={coaching.error} /> : null}
        {asRecords(coaching.data?.tasks).filter((task) => task.driverAcknowledged !== true).map((task, index) => (
          <View key={String(task.id ?? index)} style={{ gap: 10 }}>
            <Field label={textOf(task.priority, "Task")} value={textOf(task.title ?? task.description)} />
            <ActionButton label="Acknowledge" onPress={() => void api.acknowledgeDriverCoaching(task.id as number | string).then(() => coaching.refresh()).catch((error) => Alert.alert("Acknowledgement failed", error instanceof Error ? error.message : "Server rejected the action."))} variant="secondary" />
          </View>
        ))}
        {!coaching.loading && asRecords(coaching.data?.tasks).filter((task) => task.driverAcknowledged !== true).length === 0 ? <EmptyState title="All caught up" body="There are no coaching tasks waiting for acknowledgement." /> : null}
      </Panel>
    </Screen>
  );
}
