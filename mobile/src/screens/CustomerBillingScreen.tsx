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

function money(value: unknown, currency: string) {
  const amount = Number(value ?? 0);
  if (!Number.isFinite(amount)) return `${currency} 0.00`;
  try {
    return new Intl.NumberFormat(undefined, { style: "currency", currency }).format(amount);
  } catch {
    return `${currency} ${amount.toFixed(2)}`;
  }
}

export function CustomerBillingScreen() {
  const { session, api } = useSession();
  const invoices = useAsyncResource(
    async () => (await api.request.get<{ items?: JsonRecord[] }>("/api/portal/invoices")).items ?? [],
    [api],
  );
  const rows = useMemo(() => asRecords(invoices.data), [invoices.data]);
  const currency = String(session?.company.currency ?? rows[0]?.currency ?? "USD").toUpperCase();
  const outstanding = useMemo(() => rows.reduce((sum, invoice) => sum + Number(invoice.balanceDue ?? 0), 0), [rows]);
  const overdue = useMemo(() => rows.filter((invoice) => String(invoice.arStatus ?? "").startsWith("Overdue")).length, [rows]);

  return (
    <Screen>
      <HeroPanel tone={overdue > 0 ? "amber" : "blue"}>
        <SectionHeader
          eyebrow="Customer finance"
          title="Invoices"
          description="Customer-safe invoice summaries returned only for your authenticated account."
          right={<Pill label={overdue > 0 ? "Attention" : "Current"} tone={overdue > 0 ? "amber" : "green"} />}
        />
        <Row>
          <MetricCard
            label="Outstanding"
            value={invoices.loading ? "…" : money(outstanding, currency)}
            helper="Open balance"
            tone={overdue > 0 ? "amber" : "blue"}
          />
          <MetricCard
            label="Overdue"
            value={invoices.loading ? "…" : String(overdue)}
            helper={overdue > 0 ? "Needs review" : "No overdue invoices"}
            tone={overdue > 0 ? "red" : "green"}
          />
        </Row>
      </HeroPanel>

      {invoices.loading ? <LoadingState label="Loading invoices…" /> : null}
      {invoices.error ? <ErrorState title="Couldn’t load invoices" body={invoices.error} onRetry={invoices.refresh} /> : null}
      {!invoices.loading && !invoices.error && rows.length === 0 ? (
        <EmptyState title="No invoices yet" body="Invoices will appear here after your shipments are billed." />
      ) : null}

      <View style={{ gap: 12 }}>
        {rows.map((invoice, index) => {
          const invoiceCurrency = String(invoice.currency ?? currency).toUpperCase();
          const status = textOf(invoice.arStatus, "Pending");
          const tone = toneForStatus(status);
          return (
            <Panel key={String(invoice.id ?? invoice.invoiceNumber ?? index)} variant="elevated" tone={tone}>
              <Row>
                <View style={{ flex: 1, gap: 4 }}>
                  <Text style={{ color: colors.subtle, fontSize: 10, fontWeight: "800", textTransform: "uppercase", letterSpacing: 1 }}>Invoice</Text>
                  <Text style={{ color: colors.text, fontSize: 17, fontWeight: "900", letterSpacing: -0.2 }}>
                    {textOf(invoice.invoiceNumber, `Invoice ${index + 1}`)}
                  </Text>
                </View>
                <Pill label={status} tone={tone} />
              </Row>
              <Row>
                <View style={{ flex: 1 }}><Field label="Total" value={money(invoice.total, invoiceCurrency)} /></View>
                <View style={{ flex: 1 }}><Field label="Balance due" value={money(invoice.balanceDue, invoiceCurrency)} /></View>
              </Row>
              <Field label="Due date" value={textOf(invoice.dueDate)} />
            </Panel>
          );
        })}
      </View>
    </Screen>
  );
}
