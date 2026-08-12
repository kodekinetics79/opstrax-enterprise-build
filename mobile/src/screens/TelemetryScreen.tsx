import { View } from "react-native";
import { EmptyState, Field, LoadingState, Panel, Screen, SectionHeader } from "@/components/ui";
import { useSession } from "@/auth/SessionProvider";
import { useAsyncResource } from "@/hooks/useAsyncResource";

function textOf(value: unknown) {
  return value === null || value === undefined || value === "" ? "No data yet" : String(value);
}

export function TelemetryScreen() {
  const { api } = useSession();
  const telemetry = useAsyncResource(async () => api.telemetrySummary(), [api]);
  const safety = useAsyncResource(async () => api.safetyDashboard(), [api]);
  const maintenance = useAsyncResource(async () => api.maintenanceDashboard(), [api]);
  const telemetryRecord = telemetry.data as Record<string, unknown> | null;
  const telemetryKpis = telemetryRecord?.kpis as Record<string, unknown> | undefined;
  const safetyRecord = safety.data as Record<string, unknown> | null;
  const maintenanceRecord = maintenance.data as Record<string, unknown> | null;
  const maintenanceKpis = maintenanceRecord?.kpis as Record<string, unknown> | undefined;

  return (
    <Screen>
      <Panel>
        <SectionHeader eyebrow="Operational visibility" title="Telemetry, safety, and maintenance" description="This is a read-only mobile preview of live operations and fleet health." />
      </Panel>

      <Panel>
        <SectionHeader eyebrow="Live state" title="Telemetry summary" description="The app only shows the live state the backend returns." />
        {telemetry.loading ? <LoadingState label="Loading telemetry..." /> : telemetry.error ? <EmptyState title="Telemetry unavailable" body={telemetry.error} /> : null}
        {telemetry.data ? (
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
        {safety.loading ? <LoadingState label="Loading safety..." /> : safety.error ? <EmptyState title="Safety unavailable" body={safety.error} /> : null}
        {safety.data ? (
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
        {maintenance.loading ? <LoadingState label="Loading maintenance..." /> : maintenance.error ? <EmptyState title="Maintenance unavailable" body={maintenance.error} /> : null}
        {maintenance.data ? (
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
