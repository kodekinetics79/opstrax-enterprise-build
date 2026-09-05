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
  const [selectedInvoiceId, setSelectedInvoiceId] = useState<string | null>(null);
  const invoices = useAsyncResource(
    async () => (await api.request.get<{ items?: JsonRecord[] }>("/api/portal/invoices")).items ?? [],
    [api],
  );
  const invoiceDetail = useAsyncResource(
    async () => selectedInvoiceId ? api.request.get<JsonRecord>(`/api/portal/invoices/${encodeURIComponent(selectedInvoiceId)}`) : null,
    [api, selectedInvoiceId],
  );
  const rows = useMemo(() => asRecords(invoices.data), [invoices.data]);
  const currency = String(session?.company.currency ?? rows[0]?.currency ?? "USD").toUpperCase();
  const outstanding = useMemo(() => rows.reduce((sum, invoice) => sum + Number(invoice.balanceDue ?? 0), 0), [rows]);
  const overdue = useMemo(() => rows.filter((invoice) => String(invoice.arStatus ?? "").startsWith("Overdue")).length, [rows]);

  if (selectedInvoiceId) {
    const detail = invoiceDetail.data;
    const detailCurrency = String(detail?.currency ?? currency).toUpperCase();
    const status = textOf(detail?.arStatus, "Invoice");
    const lines = asRecords(detail?.lines);
    const taxes = asRecords(detail?.taxBreakdown);
    const payments = asRecords(detail?.payments);

    return (
      <Screen>
        <ActionButton label="Back to invoices" onPress={() => setSelectedInvoiceId(null)} variant="ghost" />
        <HeroPanel tone={toneForStatus(status)}>
          <SectionHeader
            eyebrow="Invoice detail"
            title={textOf(detail?.invoiceNumber, "Invoice")}
            description="Customer-safe invoice detail, tax breakdown, and payment history for your account."
            right={<Pill label={status} tone={toneForStatus(status)} />}
          />
          <Row>
            <MetricCard label="Total" value={money(detail?.total, detailCurrency)} helper="Invoice total" tone="blue" />
            <MetricCard label="Paid" value={money(detail?.amountPaid, detailCurrency)} helper="Payments received" tone="green" />
            <MetricCard label="Balance" value={money(detail?.balanceDue, detailCurrency)} helper="Remaining due" tone={Number(detail?.balanceDue ?? 0) > 0 ? "amber" : "green"} />
          </Row>
        </HeroPanel>

        {invoiceDetail.loading ? <LoadingState label="Loading invoice detail…" /> : null}
        {invoiceDetail.error ? <ErrorState title="Invoice detail unavailable" body={invoiceDetail.error} onRetry={invoiceDetail.refresh} /> : null}

        {!invoiceDetail.loading && !invoiceDetail.error && detail ? (
          <>
            <Panel variant="elevated" tone="blue">
              <SectionHeader eyebrow="Summary" title="Invoice information" description="The mobile view exposes only fields intended for the customer account." />
              <Row>
                <View style={{ flex: 1 }}><Field label="Issued" value={textOf(detail.issuedAt)} /></View>
                <View style={{ flex: 1 }}><Field label="Due" value={textOf(detail.dueAt)} /></View>
              </Row>
              <Field label="Customer" value={textOf(detail.customerName)} />
              <Field label="Seller" value={textOf(detail.sellerName)} />
              <Row>
                <View style={{ flex: 1 }}><Field label="Customer tax ID" value={textOf(detail.customerTaxId)} /></View>
                <View style={{ flex: 1 }}><Field label="Seller tax ID" value={textOf(detail.sellerTaxRegistrationNo)} /></View>
              </Row>
              <Field label="Place of supply" value={textOf(detail.placeOfSupply)} />
            </Panel>

            <Panel variant="elevated" tone="teal">
              <SectionHeader eyebrow="Charges" title="Invoice lines" description="Review what was billed before contacting support or finance." />
              {lines.length ? (
                <View style={{ gap: 12 }}>
                  {lines.map((line, index) => (
                    <Panel key={String(line.lineNo ?? index)} variant="quiet" tone="teal">
                      <Row>
                        <View style={{ flex: 1 }}><Field label={`Line ${textOf(line.lineNo, String(index + 1))}`} value={textOf(line.description)} /></View>
                        <View style={{ minWidth: 110 }}><Field label="Amount" value={money(line.amount, detailCurrency)} /></View>
                      </Row>
                      <Row>
                        <View style={{ flex: 1 }}><Field label="Charge code" value={textOf(line.chargeCode)} /></View>
                        <View style={{ flex: 1 }}><Field label="Quantity" value={`${textOf(line.quantity)} ${textOf(line.unit, "")}`.trim()} /></View>
                      </Row>
                      <Field label="Unit rate" value={money(line.unitRate, detailCurrency)} />
                    </Panel>
                  ))}
                </View>
              ) : <EmptyState title="No line detail" body="The invoice detail endpoint returned no billable lines." />}
            </Panel>

            <Panel variant="elevated" tone="violet">
              <SectionHeader eyebrow="Taxes" title="Tax breakdown" description="Grouped tax information for reconciliation and statutory review." />
              {taxes.length ? (
                <View style={{ gap: 12 }}>
                  {taxes.map((tax, index) => (
                    <Panel key={`${textOf(tax.taxCode, "tax")}-${index}`} variant="quiet" tone="violet">
                      <Row>
                        <View style={{ flex: 1 }}><Field label="Tax" value={`${textOf(tax.taxCode)} · ${textOf(tax.rate)}%`} /></View>
                        <View style={{ flex: 1 }}><Field label="Tax amount" value={money(tax.taxAmount, detailCurrency)} /></View>
                      </Row>
                      <Field label="Taxable amount" value={money(tax.taxableAmount, detailCurrency)} />
                      <Field label="Jurisdiction" value={textOf(tax.jurisdiction)} />
                    </Panel>
                  ))}
                </View>
              ) : <EmptyState title="No tax breakdown" body="No tax lines were returned for this invoice." />}
            </Panel>

            <Panel variant="elevated" tone="green">
              <SectionHeader eyebrow="Payments" title="Payment history" description="Payments shown here are tied to this invoice and customer account." />
              {payments.length ? (
                <View style={{ gap: 12 }}>
                  {payments.map((payment, index) => (
                    <Panel key={`${textOf(payment.paymentReference, "payment")}-${index}`} variant="quiet" tone="green">
                      <Row>
                        <View style={{ flex: 1 }}><Field label="Reference" value={textOf(payment.paymentReference)} /></View>
                        <View style={{ flex: 1 }}><Field label="Amount" value={money(payment.amount, String(payment.currency ?? detailCurrency).toUpperCase())} /></View>
                      </Row>
                      <Field label="Method" value={textOf(payment.paymentMethod)} />
                      <Field label="Received" value={textOf(payment.receivedAt)} />
                    </Panel>
                  ))}
                </View>
              ) : <EmptyState title="No payments recorded" body="Payment history will appear when payments are posted to this invoice." />}
            </Panel>
          </>
        ) : null}
      </Screen>
    );
  }

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
          <MetricCard label="Outstanding" value={invoices.loading ? "…" : money(outstanding, currency)} helper="Open balance" tone={overdue > 0 ? "amber" : "blue"} />
          <MetricCard label="Overdue" value={invoices.loading ? "…" : String(overdue)} helper={overdue > 0 ? "Needs review" : "No overdue invoices"} tone={overdue > 0 ? "red" : "green"} />
        </Row>
      </HeroPanel>

      {invoices.loading ? <LoadingState label="Loading invoices…" /> : null}
      {invoices.error ? <ErrorState title="Couldn’t load invoices" body={invoices.error} onRetry={invoices.refresh} /> : null}
      {!invoices.loading && !invoices.error && rows.length === 0 ? <EmptyState title="No invoices yet" body="Invoices will appear here after your shipments are billed." /> : null}

      <View style={{ gap: 12 }}>
        {rows.map((invoice, index) => {
          const invoiceCurrency = String(invoice.currency ?? currency).toUpperCase();
          const status = textOf(invoice.arStatus, "Pending");
          const tone = toneForStatus(status);
          const id = String(invoice.id ?? "").trim();
          return (
            <Pressable
              key={String(invoice.id ?? invoice.invoiceNumber ?? index)}
              accessibilityRole="button"
              accessibilityLabel={`Open ${textOf(invoice.invoiceNumber, `invoice ${index + 1}`)}`}
              disabled={!id}
              onPress={() => id && setSelectedInvoiceId(id)}
              style={({ pressed }) => ({ opacity: pressed ? 0.86 : 1, transform: [{ scale: pressed ? 0.993 : 1 }] })}
            >
              <Panel variant="elevated" tone={tone}>
                <Row>
                  <View style={{ flex: 1, gap: 4 }}>
                    <Text style={{ color: colors.subtle, fontSize: 10, fontWeight: "800", textTransform: "uppercase", letterSpacing: 1 }}>Invoice</Text>
                    <Text style={{ color: colors.text, fontSize: 17, fontWeight: "900", letterSpacing: -0.2 }}>{textOf(invoice.invoiceNumber, `Invoice ${index + 1}`)}</Text>
                  </View>
                  <Pill label={status} tone={tone} />
                </Row>
                <Row>
                  <View style={{ flex: 1 }}><Field label="Total" value={money(invoice.total, invoiceCurrency)} /></View>
                  <View style={{ flex: 1 }}><Field label="Balance due" value={money(invoice.balanceDue, invoiceCurrency)} /></View>
                </Row>
                <Field label="Due date" value={textOf(invoice.dueAt)} />
                <Text style={{ color: colors.blue, fontSize: 12, fontWeight: "800" }}>{id ? "View charges, taxes, and payments  ›" : "Detail unavailable"}</Text>
              </Panel>
            </Pressable>
          );
        })}
      </View>
    </Screen>
  );
}
