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
  MetricCard,
  Panel,
  Pill,
  Row,
  Screen,
  SectionHeader,
  colors,
  toneForStatus,
} from "@/components/ui";

function money(value: unknown, currency: string) {
  const amount = Number(value ?? 0);
  if (!Number.isFinite(amount)) return `${currency} 0.00`;
  try {
    return new Intl.NumberFormat(undefined, { style: "currency", currency }).format(amount);
  } catch {
    return `${currency} ${amount.toFixed(2)}`;
  }
}

function isActiveShipment(job: JsonRecord) {
  return !/delivered|completed|cancelled/i.test(String(job.status ?? ""));
}

export function CustomerHomeScreen() {
  const { session, api } = useSession();
  const jobs = useAsyncResource(
    async () => (await api.request.get<{ items?: JsonRecord[] }>("/api/portal/jobs")).items ?? [],
    [api],
  );
  const invoices = useAsyncResource(
    async () => (await api.request.get<{ items?: JsonRecord[] }>("/api/portal/invoices")).items ?? [],
    [api],
  );

  const shipmentRows = useMemo(() => asRecords(jobs.data), [jobs.data]);
  const invoiceRows = useMemo(() => asRecords(invoices.data), [invoices.data]);
  const activeShipments = useMemo(() => shipmentRows.filter(isActiveShipment), [shipmentRows]);
  const outstanding = useMemo(
    () => invoiceRows.reduce((sum, invoice) => sum + Number(invoice.balanceDue ?? 0), 0),
    [invoiceRows],
  );
  const overdue = useMemo(
    () => invoiceRows.filter((invoice) => String(invoice.arStatus ?? "").startsWith("Overdue")).length,
    [invoiceRows],
  );
  const nextShipment = activeShipments[0];
  const currency = String(session?.company.currency ?? invoiceRows[0]?.currency ?? "USD").toUpperCase();

  return (
    <Screen>
      <Panel>
        <SectionHeader
          eyebrow={session?.company.name}
          title={`Welcome, ${session?.user.name ?? "customer"}`}
          description="Your shipments, delivery status, proof, and billing are scoped to your customer account only."
          right={<Pill label="Customer" tone="teal" />}
        />
        <Row>
          <MetricCard label="Active shipments" value={jobs.loading ? "…" : String(activeShipments.length)} tone="teal" />
          <MetricCard label="Outstanding" value={invoices.loading ? "…" : money(outstanding, currency)} tone={overdue > 0 ? "amber" : "blue"} />
          <MetricCard label="Overdue" value={invoices.loading ? "…" : String(overdue)} tone={overdue > 0 ? "red" : "green"} />
        </Row>
      </Panel>

      <Panel>
        <SectionHeader
          eyebrow="Next delivery"
          title="What needs your attention"
          description="Live customer-safe shipment data from the OpsTrax backend."
        />
        {jobs.loading ? <LoadingState label="Loading your shipments…" /> : null}
        {jobs.error ? <ErrorState title="Shipments unavailable" body={jobs.error} onRetry={jobs.refresh} /> : null}
        {!jobs.loading && !jobs.error && !nextShipment ? (
          <EmptyState title="No active shipments" body="New and in-progress shipments will appear here automatically." />
        ) : null}
        {nextShipment ? (
          <View style={{ gap: 10 }}>
            <Row>
              <View style={{ flex: 1, gap: 4 }}>
                <Text style={{ color: colors.text, fontSize: 19, fontWeight: "900" }}>
                  {textOf(nextShipment.jobNumber ?? nextShipment.trackingCode, "Shipment")}
                </Text>
                <Text style={{ color: colors.muted, fontSize: 13, lineHeight: 18 }}>
                  {textOf(nextShipment.pickupAddress, "Pickup pending")} → {textOf(nextShipment.dropoffAddress, "Destination pending")}
                </Text>
              </View>
              <Pill label={textOf(nextShipment.status, "Active")} tone={toneForStatus(String(nextShipment.status ?? ""))} />
            </Row>
            <Field label="Estimated arrival" value={textOf(nextShipment.eta ?? nextShipment.scheduledEnd)} />
            <Field label="Tracking code" value={textOf(nextShipment.trackingCode)} />
          </View>
        ) : null}
      </Panel>

      <Panel>
        <SectionHeader eyebrow="Account boundary" title="Private to your organization" description="The server enforces both tenant and customer-account ownership before returning these records." />
        <Field label="Organization" value={session?.company.name} />
        <Field label="Account" value={session?.user.email} />
      </Panel>
    </Screen>
  );
}
