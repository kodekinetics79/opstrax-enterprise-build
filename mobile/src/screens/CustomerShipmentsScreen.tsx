import { useMemo, useState } from "react";
import { Pressable, Text, View } from "react-native";
import { useSession } from "@/auth/SessionProvider";
import { useAsyncResource } from "@/hooks/useAsyncResource";
import { asRecords, textOf } from "@/data/records";
import type { JsonRecord } from "@/types";
import {
  ActionButton,
  EmptyState,
  ErrorState,
  Field,
  HeroPanel,
  LoadingState,
  MetricCard,
  Panel,
  Pill,
  Row,
  Screen,
  SectionHeader,
  colors,
  toneForStatus,
} from "@/components/ui";

function isActiveShipment(job: JsonRecord) {
  return !/delivered|completed|cancelled/i.test(String(job.status ?? ""));
}

type PortalJobDetail = {
  job?: JsonRecord;
  statusTimeline?: JsonRecord[];
  proofs?: JsonRecord[];
};

export function CustomerShipmentsScreen() {
  const { api } = useSession();
  const [selectedJobId, setSelectedJobId] = useState<number | null>(null);
  const jobs = useAsyncResource(
    async () => (await api.request.get<{ items?: JsonRecord[] }>("/api/portal/jobs")).items ?? [],
    [api],
  );
  const detail = useAsyncResource(
    async () => selectedJobId ? api.request.get<PortalJobDetail>(`/api/portal/jobs/${selectedJobId}`) : null,
    [api, selectedJobId],
  );
  const rows = useMemo(() => asRecords(jobs.data), [jobs.data]);
  const active = useMemo(() => rows.filter(isActiveShipment), [rows]);
  const completed = useMemo(() => rows.filter((job) => /delivered|completed/i.test(String(job.status ?? ""))), [rows]);

  if (selectedJobId) {
    const shipment = detail.data?.job;
    const timeline = asRecords(detail.data?.statusTimeline);
    const proofs = asRecords(detail.data?.proofs);
    const status = String(shipment?.status ?? "");

    return (
      <Screen>
        <ActionButton label="Back to shipments" onPress={() => setSelectedJobId(null)} variant="ghost" />
        <HeroPanel tone={toneForStatus(status)}>
          <SectionHeader
            eyebrow="Shipment detail"
            title={textOf(shipment?.jobNumber ?? shipment?.trackingCode, `Shipment #${selectedJobId}`)}
            description={`${textOf(shipment?.pickupAddress, "Pickup pending")} → ${textOf(shipment?.dropoffAddress, "Destination pending")}`}
            right={<Pill label={textOf(shipment?.status, "Loading")} tone={toneForStatus(status)} />}
          />
          <Row>
            <MetricCard label="ETA" value={textOf(shipment?.eta ?? shipment?.scheduledEnd)} helper="Current estimate" tone="teal" />
            <MetricCard label="SLA" value={textOf(shipment?.slaStatus, "Not provided")} helper="Customer-safe status" tone={toneForStatus(String(shipment?.slaStatus ?? ""))} />
          </Row>
        </HeroPanel>

        {detail.loading ? <LoadingState label="Loading shipment detail…" /> : null}
        {detail.error ? <ErrorState title="Shipment detail unavailable" body={detail.error} onRetry={detail.refresh} /> : null}

        {!detail.loading && !detail.error && shipment ? (
          <>
            <Panel variant="elevated" tone="blue">
              <SectionHeader eyebrow="Tracking" title="Delivery details" description="Only customer-safe fields from your owned shipment are returned." />
              <Field label="Tracking code" value={textOf(shipment.trackingCode)} />
              <Row>
                <View style={{ flex: 1 }}><Field label="Pickup window" value={textOf(shipment.scheduledStart)} /></View>
                <View style={{ flex: 1 }}><Field label="Delivery window" value={textOf(shipment.scheduledEnd)} /></View>
              </Row>
              <Field label="Pickup" value={textOf(shipment.pickupAddress)} />
              <Field label="Delivery" value={textOf(shipment.dropoffAddress)} />
            </Panel>

            <Panel variant="elevated" tone="teal">
              <SectionHeader eyebrow="Timeline" title="Shipment progress" description="Milestones are derived by the customer portal service from the owned shipment record." />
              {timeline.length ? (
                <View style={{ gap: 10 }}>
                  {timeline.map((item, index) => {
                    const stageStatus = textOf(item.status ?? item.stage, "Milestone");
                    return (
                      <View key={`${stageStatus}-${index}`} style={{ flexDirection: "row", gap: 12, alignItems: "flex-start" }}>
                        <View style={{ width: 13, height: 13, borderRadius: 13, marginTop: 5, borderWidth: 2, borderColor: colors.teal, backgroundColor: index === 1 ? colors.teal : colors.backgroundDeep }} />
                        <View style={{ flex: 1 }}>
                          <Field
                            label={textOf(item.stage, `Milestone ${index + 1}`)}
                            value={textOf(item.at ?? item.status ?? item.eta)}
                          />
                        </View>
                      </View>
                    );
                  })}
                </View>
              ) : <EmptyState title="No timeline yet" body="The portal did not return shipment milestones for this record." />}
            </Panel>

            <Panel variant="elevated" tone="green">
              <SectionHeader eyebrow="Proof" title="Pickup and delivery evidence" description="Proof metadata is customer-safe and scoped through the shipment you own." />
              {proofs.length ? (
                <View style={{ gap: 12 }}>
                  {proofs.map((proof, index) => (
                    <Panel key={`${textOf(proof.proofType, "proof")}-${index}`} variant="quiet" tone="green">
                      <Row>
                        <View style={{ flex: 1 }}><Field label="Proof type" value={textOf(proof.proofType)} /></View>
                        <Pill label={textOf(proof.status, "Recorded")} tone={toneForStatus(String(proof.status ?? ""))} />
                      </Row>
                      <Field label="Completed" value={textOf(proof.completedAt)} />
                      <Field label="Receiver" value={textOf(proof.receiverName)} />
                      <Field label="Evidence items" value={String(asRecords(proof.artifacts).length)} />
                    </Panel>
                  ))}
                </View>
              ) : <EmptyState title="No proof available yet" body="Pickup or delivery proof will appear after it is recorded for this shipment." />}
            </Panel>
          </>
        ) : null}
      </Screen>
    );
  }

  return (
    <Screen>
      <HeroPanel tone="blue">
        <SectionHeader
          eyebrow="Shipment visibility"
          title="Your shipments"
          description="Live customer-safe shipment records returned only for your authenticated account."
          right={<Pill label="Private" tone="green" />}
        />
        <Row>
          <MetricCard label="Active" value={jobs.loading ? "…" : String(active.length)} helper="In progress" tone="teal" />
          <MetricCard label="Delivered" value={jobs.loading ? "…" : String(completed.length)} helper="Completed" tone="green" />
          <MetricCard label="Total" value={jobs.loading ? "…" : String(rows.length)} helper="Visible to you" tone="blue" />
        </Row>
      </HeroPanel>

      {jobs.loading ? <LoadingState label="Loading shipment status…" /> : null}
      {jobs.error ? <ErrorState title="Couldn’t load shipments" body={jobs.error} onRetry={jobs.refresh} /> : null}
      {!jobs.loading && !jobs.error && rows.length === 0 ? (
        <EmptyState title="No shipments yet" body="Your current and historical shipments will appear here once created." />
      ) : null}

      <View style={{ gap: 12 }}>
        {rows.map((job, index) => {
          const tone = toneForStatus(String(job.status ?? ""));
          const id = Number(job.id);
          const canOpen = Number.isFinite(id) && id > 0;
          return (
            <Pressable
              key={String(job.id ?? job.jobNumber ?? index)}
              accessibilityRole="button"
              accessibilityLabel={`Open ${textOf(job.jobNumber ?? job.trackingCode, `shipment ${index + 1}`)}`}
              disabled={!canOpen}
              onPress={() => canOpen && setSelectedJobId(id)}
              style={({ pressed }) => ({ opacity: pressed ? 0.86 : 1, transform: [{ scale: pressed ? 0.993 : 1 }] })}
            >
              <Panel variant="elevated" tone={tone}>
                <Row>
                  <View style={{ flex: 1, minWidth: 170, gap: 5 }}>
                    <Text style={{ color: colors.text, fontSize: 17, fontWeight: "900", letterSpacing: -0.25 }}>
                      {textOf(job.jobNumber ?? job.trackingCode, `Shipment ${index + 1}`)}
                    </Text>
                    <Text style={{ color: colors.muted, fontSize: 12.5, lineHeight: 18 }}>
                      {textOf(job.pickupAddress, "Pickup pending")} → {textOf(job.dropoffAddress, "Destination pending")}
                    </Text>
                  </View>
                  <Pill label={textOf(job.status, "Pending")} tone={tone} />
                </Row>
                <Field label="ETA" value={textOf(job.eta ?? job.scheduledEnd)} />
                <Row>
                  <View style={{ flex: 1 }}><Field label="Pickup" value={textOf(job.scheduledStart)} /></View>
                  <View style={{ flex: 1 }}><Field label="Delivery" value={textOf(job.scheduledEnd)} /></View>
                </Row>
                <Field label="Tracking code" value={textOf(job.trackingCode)} />
                <Text style={{ color: colors.blue, fontSize: 12, fontWeight: "800" }}>{canOpen ? "View timeline and proof  ›" : "Detail unavailable"}</Text>
              </Panel>
            </Pressable>
          );
        })}
      </View>
    </Screen>
  );
}
