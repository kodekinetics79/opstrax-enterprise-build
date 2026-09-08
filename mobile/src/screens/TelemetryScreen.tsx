import { View } from "react-native";
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
} from "@/components/ui";
import { useSession } from "@/auth/SessionProvider";
import { useAsyncResource } from "@/hooks/useAsyncResource";

function textOf(value: unknown) {
  return value === null || value === undefined || value === "" ? "No data yet" : String(value);
}

export function TelemetryScreen() {
  const { api, hasPermission } = useSession();
  const canReadTelemetry = ["telemetry.live_state.read", "telemetry.live-state.read", "telemetry.alerts.read", "dashboard:view", "map:view", "fleet:view", "telematics:gps:view"].some(hasPermission);
  const canReadSafety = hasPermission("safety:view");
  const canReadMaintenance = hasPermission("maintenance:view");
  const telemetry = useAsyncResource(async () => (canReadTelemetry ? api.telemetrySummary() : null), [api, canReadTelemetry]);
  const safety = useAsyncResource(async () => (canReadSafety ? api.safetyDashboard() : null), [api, canReadSafety]);
  const maintenance = useAsyncResource(async () => (canReadMaintenance ? api.maintenanceDashboard() : null), [api, canReadMaintenance]);
  const telemetryRecord = telemetry.data as Record<string, unknown> | null;
  const telemetryKpis = telemetryRecord?.kpis as Record<string, unknown> | undefined;
  const safetyRecord = safety.data as Record<string, unknown> | null;
  const maintenanceRecord = maintenance.data as Record<string, unknown> | null;
  const maintenanceKpis = maintenanceRecord?.kpis as Record<string, unknown> | undefined;
  const telemetryPayloadError = typeof telemetryRecord?.error === "string" ? telemetryRecord.error : null;

  return (
    <Screen>
      <HeroPanel tone="violet">
        <SectionHeader
          eyebrow="Operational visibility"
          title="Fleet health"
          description="A mobile command view of live telemetry, safety, and maintenance signals returned by the backend."
          right={<Pill label="Read only" tone="blue" />}
        />
        <Row>
          <MetricCard label="Connected" value={canReadTelemetry && telemetry.data ? textOf(telemetryKpis?.connectedUnits) : "Scoped"} helper="Assets reporting" tone="teal" />
          <MetricCard label="Open alerts" value={canReadTelemetry && telemetry.data ? textOf(telemetryKpis?.openAlerts) : "Scoped"} helper="Needs review" tone="amber" />
          <MetricCard label="Critical defects" value={canReadMaintenance && maintenance.data ? textOf(maintenanceKpis?.criticalOpenDefects) : "Scoped"} helper="Maintenance risk" tone="red" />
        </Row>
      </HeroPanel>

      <Panel variant="elevated" tone="teal">
        <SectionHeader eyebrow="Live state" title="Telemetry summary" description="The app only shows the live state the backend returns." />
        {!canReadTelemetry ? (
          <EmptyState title="Telemetry not available" body="This authenticated session does not grant live telemetry access." />
        ) : telemetry.loading ? (
          <LoadingState label="Loading telemetry..." />
        ) : telemetry.error || telemetryPayloadError ? (
          <ErrorState title="Telemetry unavailable" body={telemetry.error ?? telemetryPayloadError ?? "Unable to load telemetry."} onRetry={telemetry.refresh} />
        ) : telemetry.data ? (
          <View style={{ gap: 10 }}>
            <Field label="As of" value={textOf(telemetryRecord?.asOf)} />
            <Row>
              <View style={{ flex: 1 }}><Field label="Open alerts" value={textOf(telemetryKpis?.openAlerts)} /></View>
              <View style={{ flex: 1 }}><Field label="Connected assets" value={textOf(telemetryKpis?.connectedUnits)} /></View>
            </Row>
            <Field label="Stale assets" value={textOf(telemetryKpis?.staleUnits)} />
          </View>
        ) : (
          <EmptyState title="No telemetry yet" body="The telemetry API returned no summary object." />
        )}
      </Panel>

      <Panel variant="elevated" tone="amber">
        <SectionHeader eyebrow="Safety" title="Safety dashboard" description="Safety remains backend-enforced and tenant-scoped." />
        {!canReadSafety ? (
          <EmptyState title="Safety not available" body="This authenticated session does not grant safety dashboard access." />
        ) : safety.loading ? (
          <LoadingState label="Loading safety..." />
        ) : safety.error ? (
          <ErrorState title="Safety unavailable" body={safety.error} onRetry={safety.refresh} />
        ) : safety.data ? (
          <View style={{ gap: 10 }}>
            <Field label="Fleet safety score" value={textOf(safetyRecord?.fleetSafetyScore)} />
            <Row>
              <View style={{ flex: 1 }}><Field label="Open events" value={textOf(safetyRecord?.openEvents)} /></View>
              <View style={{ flex: 1 }}><Field label="Critical open" value={textOf(safetyRecord?.criticalOpen)} /></View>
            </Row>
          </View>
        ) : (
          <EmptyState title="No safety dashboard" body="The safety dashboard is only displayed if the backend returns a payload." />
        )}
      </Panel>

      <Panel variant="elevated" tone="blue">
        <SectionHeader eyebrow="Maintenance" title="Maintenance dashboard" description="A manager can preview maintenance state without opening the full desktop command center." />
        {!canReadMaintenance ? (
          <EmptyState title="Maintenance not available" body="This authenticated session does not grant maintenance dashboard access." />
        ) : maintenance.loading ? (
          <LoadingState label="Loading maintenance..." />
        ) : maintenance.error ? (
          <ErrorState title="Maintenance unavailable" body={maintenance.error} onRetry={maintenance.refresh} />
        ) : maintenance.data ? (
          <View style={{ gap: 10 }}>
            <Field label="Fleet availability" value={textOf(maintenanceKpis?.fleetAvailabilityPct)} />
            <Row>
              <View style={{ flex: 1 }}><Field label="Open work orders" value={textOf(maintenanceKpis?.openWorkOrders)} /></View>
              <View style={{ flex: 1 }}><Field label="Critical open defects" value={textOf(maintenanceKpis?.criticalOpenDefects)} /></View>
            </Row>
          </View>
        ) : (
          <EmptyState title="No maintenance dashboard" body="The maintenance dashboard is only displayed if the backend returns a payload." />
        )}
      </Panel>
    </Screen>
  );
}
