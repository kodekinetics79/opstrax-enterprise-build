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
  LoadingState,
  Panel,
  Pill,
  Row,
  Screen,
  SectionHeader,
  colors,
  toneForStatus,
} from "@/components/ui";

export function CustomerShipmentsScreen() {
  const { api } = useSession();
  const jobs = useAsyncResource(
    async () => (await api.request.get<{ items?: JsonRecord[] }>("/api/portal/jobs")).items ?? [],
    [api],
  );
  const rows = useMemo(() => asRecords(jobs.data), [jobs.data]);

  return (
    <Screen>
      <Panel>
        <SectionHeader
          eyebrow="Shipment visibility"
          title="Your shipments"
          description="Only shipments bound to your customer account are returned by the server."
        />
      </Panel>

      <Panel>
        {jobs.loading ? <LoadingState label="Loading shipment status…" /> : null}
        {jobs.error ? <ErrorState title="Couldn’t load shipments" body={jobs.error} onRetry={jobs.refresh} /> : null}
        {!jobs.loading && !jobs.error && rows.length === 0 ? (
          <EmptyState title="No shipments yet" body="Your current and historical shipments will appear here once created." />
        ) : null}
        <View style={{ gap: 12 }}>
          {rows.map((job, index) => (
            <View
              key={String(job.id ?? job.jobNumber ?? index)}
              style={{ borderRadius: 18, borderWidth: 1, borderColor: colors.border, backgroundColor: colors.panelAlt, padding: 14, gap: 10 }}
            >
              <Row>
                <View style={{ flex: 1, minWidth: 170, gap: 4 }}>
                  <Text style={{ color: colors.text, fontSize: 16, fontWeight: "900" }}>
                    {textOf(job.jobNumber ?? job.trackingCode, `Shipment ${index + 1}`)}
                  </Text>
                  <Text style={{ color: colors.muted, fontSize: 12, lineHeight: 18 }}>
                    {textOf(job.pickupAddress, "Pickup pending")} → {textOf(job.dropoffAddress, "Destination pending")}
                  </Text>
                </View>
                <Pill label={textOf(job.status, "Pending")} tone={toneForStatus(String(job.status ?? ""))} />
              </Row>
              <Field label="ETA" value={textOf(job.eta ?? job.scheduledEnd)} />
              <Row>
                <View style={{ flex: 1 }}><Field label="Pickup" value={textOf(job.scheduledStart)} /></View>
                <View style={{ flex: 1 }}><Field label="Delivery" value={textOf(job.scheduledEnd)} /></View>
              </Row>
              <Field label="Tracking code" value={textOf(job.trackingCode)} />
            </View>
          ))}
        </View>
      </Panel>
    </Screen>
  );
}
