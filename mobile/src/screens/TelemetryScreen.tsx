import { View } from "react-native";
import { EmptyState, ErrorState, Field, LoadingState, Panel, Screen, SectionHeader } from "@/components/ui";
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
      <Panel>
        <SectionHeader eyebrow="Operational visibility" title="Telemetry, safety, and maintenance" description="This is a read-only mobile preview of live operations and fleet health." />
      </Panel>

      <Panel>
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
            <Field label="Open alerts" value={textOf(telemetryKpis?.openAlerts)} />
            <Field label="Connected assets" value={textOf(telemetryKpis?.connectedUnits)} />
            <Field label="Stale assets" value={textOf(telemetryKpis?.staleUnits)} />
          </View>
        ) : (
          <EmptyState title="No telemetry yet" body="The telemetry API returned no summary object." />
        )}
      </Panel>

      <Panel>
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
            <Field label="Open events" value={textOf(safetyRecord?.openEvents)} />
            <Field label="Critical open" value={textOf(safetyRecord?.criticalOpen)} />
          </View>
        ) : (
          <EmptyState title="No safety dashboard" body="The safety dashboard is only displayed if the backend returns a payload." />
        )}
      </Panel>

      <Panel>
        <SectionHeader eyebrow="Maintenance" title="Maintenance dashboard" description="A mobile manager can preview maintenance state without the full web portal." />
        {!canReadMaintenance ? (
          <EmptyState title="Maintenance not available" body="This authenticated session does not grant maintenance dashboard access." />
        ) : maintenance.loading ? (
          <LoadingState label="Loading maintenance..." />
        ) : maintenance.error ? (
          <ErrorState title="Maintenance unavailable" body={maintenance.error} onRetry={maintenance.refresh} />
        ) : maintenance.data ? (
          <View style={{ gap: 10 }}>
            <Field label="Fleet availability" value={textOf(maintenanceKpis?.fleetAvailabilityPct)} />
            <Field label="Open work orders" value={textOf(maintenanceKpis?.openWorkOrders)} />
            <Field label="Critical open defects" value={textOf(maintenanceKpis?.criticalOpenDefects)} />
          </View>
        ) : (
          <EmptyState title="No maintenance dashboard" body="The maintenance dashboard is only displayed if the backend returns a payload." />
        )}
      </Panel>
    </Screen>
  );
}
