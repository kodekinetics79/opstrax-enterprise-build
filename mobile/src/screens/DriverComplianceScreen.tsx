import { useEffect, useMemo, useState } from "react";
import { Alert, Pressable, Text, View } from "react-native";
import * as Haptics from "expo-haptics";
import {
  ActionButton,
  EmptyState,
  ErrorState,
  Field,
  HeroPanel,
  Input,
  LoadingState,
  MetricCard,
  Panel,
  Pill,
  Row,
  Screen,
  SectionHeader,
  colors,
} from "@/components/ui";
import { useSession } from "@/auth/SessionProvider";
import { useAsyncResource } from "@/hooks/useAsyncResource";
import { clearSecureDraft, readSecureDraft, secureDraftKey, writeSecureDraft } from "@/storage/secureDrafts";
import { asRecords, numberOf, textOf, titleCase } from "@/data/records";
import type { JsonRecord } from "@/types";

const ATTESTATION = "I certify that this DVIR is true and correct and that I completed this inspection.";

type ChecklistResult = { result: "pass" | "fail"; severity: "minor" | "major" | "critical"; notes: string };
type DvirDraft = { results: Record<string, ChecklistResult>; attested: boolean; idempotencyKey: string };

function newDvirIdempotencyKey(company: string, user: string, vehicle: string) {
  return ["mobile-dvir", company, user, vehicle, Date.now().toString(36), Math.random().toString(36).slice(2, 10)].join(":").slice(0, 100);
}

export function DriverComplianceScreen() {
  const { api, session } = useSession();
  const profile = useAsyncResource(() => api.driverMe(), [api]);
  const current = useAsyncResource(() => api.driverCurrentAssignment(), [api]);
  const templates = useAsyncResource(() => api.driverDvirTemplates(), [api]);
  const hos = useAsyncResource(() => api.driverHos(), [api]);
  const coaching = useAsyncResource(() => api.driverCoaching(), [api]);
  const [results, setResults] = useState<Record<string, ChecklistResult>>({});
  const [attested, setAttested] = useState(false);
  const [idempotencyKey, setIdempotencyKey] = useState("");
  const [draftReady, setDraftReady] = useState(false);
  const [busy, setBusy] = useState(false);

  const template = asRecords(templates.data)[0];
  const items = useMemo(() => asRecords(template?.items), [template]);
  const assignment = current.data?.assignment;
  const vehicleId = assignment?.vehicleId ?? profile.data?.driver?.vehicleId;
  const vehicleCode = assignment?.vehicleCode ?? profile.data?.driver?.vehicleCode;
  const completed = items.length > 0 && items.every((item, index) => results[String(item.id ?? index)]?.result);
  const blocked = profile.data?.vehicleBlocking?.blocked === true;
  const lowDrive = numberOf(hos.data?.remainingDriveHours) !== null && numberOf(hos.data?.remainingDriveHours)! < 3;
  const companyScope = String(session?.company.id ?? session?.company.code ?? "unknown");
  const userScope = String(session?.user.id ?? "unknown");
  const vehicleScope = String(vehicleId ?? "unknown");
  const dvirDraftKey = vehicleId && template
    ? secureDraftKey("driver-dvir", session?.company.id ?? session?.company.code, session?.user.id, `${vehicleScope}-${String(template.id ?? template.templateName ?? "template")}`)
    : null;

  useEffect(() => {
    let active = true;
    setDraftReady(false);
    if (!dvirDraftKey) return () => { active = false; };
    void readSecureDraft<DvirDraft>(dvirDraftKey).then((draft) => {
      if (!active) return;
      if (draft?.idempotencyKey) {
        setResults(draft.results ?? {});
        setAttested(Boolean(draft.attested));
        setIdempotencyKey(draft.idempotencyKey);
      } else {
        setResults({});
        setAttested(false);
        setIdempotencyKey(newDvirIdempotencyKey(companyScope, userScope, vehicleScope));
      }
      setDraftReady(true);
    });
    return () => { active = false; };
  }, [companyScope, dvirDraftKey, userScope, vehicleScope]);

  useEffect(() => {
    if (!dvirDraftKey || !draftReady || !idempotencyKey) return;
    const timer = setTimeout(() => {
      const hasDraft = Object.keys(results).length > 0 || attested;
      const action = hasDraft
        ? writeSecureDraft<DvirDraft>(dvirDraftKey, { results, attested, idempotencyKey })
        : clearSecureDraft(dvirDraftKey);
      void action.catch(() => undefined);
    }, 250);
    return () => clearTimeout(timer);
  }, [attested, draftReady, dvirDraftKey, idempotencyKey, results]);

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
    if (!vehicleId || !profile.data?.driver?.id || !template || !idempotencyKey) return;
    setBusy(true);
    try {
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
      if (dvirDraftKey) await clearSecureDraft(dvirDraftKey).catch(() => undefined);
      setResults({});
      setAttested(false);
      setIdempotencyKey(newDvirIdempotencyKey(companyScope, userScope, vehicleScope));
      profile.refresh();
      Alert.alert("DVIR submitted", "Your inspection is recorded. Critical failures automatically block the vehicle.");
    } catch (error) {
      Alert.alert("DVIR submission failed", error instanceof Error ? `${error.message} Your inspection draft and retry identity remain saved on this device.` : "The server rejected the inspection. Your draft remains saved.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <Screen>
      <HeroPanel tone={blocked ? "red" : lowDrive ? "amber" : "teal"}>
        <SectionHeader
          eyebrow="Compliance"
          title="Safe and legal to drive"
          description="Vehicle status, inspection, coaching, and HOS data are identity-scoped by the backend. Missing certified data is shown as unavailable."
          right={<Pill label={blocked ? "Vehicle blocked" : lowDrive ? "Time attention" : "Driver ready"} tone={blocked ? "red" : lowDrive ? "amber" : "green"} />}
        />
        <Row>
          <MetricCard label="Vehicle" value={textOf(vehicleCode, "Unassigned")} helper={blocked ? "Out of service" : "Assigned unit"} tone={blocked ? "red" : "teal"} />
          <MetricCard label="Drive hours" value={hos.data?.dataAvailable === false ? "Unavailable" : `${textOf(hos.data?.remainingDriveHours)}h`} helper={hos.data?.dataAvailable === false ? "No certified feed" : "Remaining"} tone={lowDrive ? "amber" : "blue"} />
          <MetricCard label="Coaching" value={String(coaching.data?.pendingCount ?? 0)} helper="Waiting items" tone={(coaching.data?.pendingCount ?? 0) > 0 ? "amber" : "green"} />
        </Row>
      </HeroPanel>

      {profile.error || current.error ? <ErrorState title="Compliance profile unavailable" body={profile.error ?? current.error ?? "Unknown error"} /> : null}
      {blocked ? <ErrorState title="Vehicle blocked" body={profile.data?.vehicleBlocking?.reason ?? "Do not operate this vehicle until the blocking condition is cleared."} /> : null}

      <Panel variant="elevated" tone={lowDrive ? "amber" : "blue"}>
        <SectionHeader
          eyebrow="Hours of service"
          title={textOf(hos.data?.hosStatus, "ELD status unavailable")}
          description="HOS is shown only when the backend has a paired driver record and usable source data."
        />
        {hos.loading ? <LoadingState label="Loading HOS…" /> : hos.error ? <ErrorState title="HOS unavailable" body={hos.error} /> : (
          <Row>
            <MetricCard label="Drive" value={hos.data?.dataAvailable === false ? "No data" : `${textOf(hos.data?.remainingDriveHours)}h`} helper="Remaining" tone={lowDrive ? "amber" : "teal"} />
            <MetricCard label="Shift" value={hos.data?.dataAvailable === false ? "No data" : `${textOf(hos.data?.remainingShiftHours)}h`} helper="Remaining" tone="blue" />
            <MetricCard label="Cycle" value={hos.data?.dataAvailable === false ? "No data" : `${textOf(hos.data?.remainingCycleHours)}h`} helper="Remaining" tone="green" />
          </Row>
        )}
      </Panel>

      <Panel variant="solid" tone="teal">
        <SectionHeader
          eyebrow="Pre-trip DVIR"
          title={textOf(template?.templateName, "Inspection checklist")}
          description="Every item must be marked. Critical failures can immediately place the vehicle out of service. Draft state and retry identity are encrypted until submission succeeds."
          right={<Pill label={textOf(vehicleCode, "No vehicle")} tone={vehicleId ? "teal" : "red"} />}
        />
        {templates.loading ? <LoadingState label="Loading tenant checklist…" /> : templates.error ? <ErrorState title="Checklist unavailable" body={templates.error} /> : null}
        {!templates.loading && items.length === 0 ? <EmptyState title="No active DVIR template" body="Ask the fleet administrator to publish a tenant inspection template." /> : null}
        <View style={{ gap: 10 }}>
          {items.map((item, index) => {
            const key = String(item.id ?? index);
            const value = results[key];
            return (
              <View
                key={key}
                style={{
                  gap: 9,
                  padding: 13,
                  borderRadius: 18,
                  borderWidth: 1,
                  borderColor: value?.result === "fail" ? `${colors.red}66` : colors.border,
                  backgroundColor: value?.result === "fail" ? `${colors.red}0d` : "rgba(255,255,255,0.028)",
                }}
              >
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
            {Object.keys(results).length > 0 ? <Pill label="Inspection draft saved securely" tone="blue" /> : null}
            <Pressable
              accessibilityRole="checkbox"
              accessibilityState={{ checked: attested }}
              onPress={() => setAttested((value) => !value)}
              style={{ flexDirection: "row", gap: 10, alignItems: "flex-start", paddingVertical: 4 }}
            >
              <View style={{ width: 26, height: 26, borderRadius: 8, borderWidth: 1, borderColor: attested ? colors.teal : colors.borderStrong, backgroundColor: attested ? colors.teal : "rgba(255,255,255,0.02)", alignItems: "center", justifyContent: "center" }}>
                <Text style={{ color: colors.backgroundDeep, fontWeight: "900" }}>{attested ? "✓" : ""}</Text>
              </View>
              <Text style={{ flex: 1, color: colors.muted, lineHeight: 20 }}>{ATTESTATION}</Text>
            </Pressable>
            <ActionButton label={busy ? "Submitting inspection…" : "Submit DVIR"} onPress={() => void submitDvir()} disabled={busy || !vehicleId || !completed || !attested || !idempotencyKey} />
          </>
        ) : null}
      </Panel>

      <Panel variant="elevated" tone="violet">
        <SectionHeader eyebrow="Coaching" title="Required acknowledgements" description="Only coaching tasks for the authenticated driver are returned." />
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
