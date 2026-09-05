import { useMemo } from "react";
import { Text, View } from "react-native";
import { useSession } from "@/auth/SessionProvider";
import { useAsyncResource } from "@/hooks/useAsyncResource";
import { asRecords, textOf } from "@/data/records";
import type { JsonRecord } from "@/types";
import {
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

export function CustomerShipmentsScreen() {
  const { api } = useSession();
  const jobs = useAsyncResource(
    async () => (await api.request.get<{ items?: JsonRecord[] }>("/api/portal/jobs")).items ?? [],
    [api],
  );
  const rows = useMemo(() => asRecords(jobs.data), [jobs.data]);
  const active = useMemo(() => rows.filter(isActiveShipment), [rows]);
  const completed = useMemo(() => rows.filter((job) => /delivered|completed/i.test(String(job.status ?? ""))), [rows]);

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
          return (
            <Panel key={String(job.id ?? job.jobNumber ?? index)} variant="elevated" tone={tone}>
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
            </Panel>
          );
        })}
      </View>
    </Screen>
  );
}
