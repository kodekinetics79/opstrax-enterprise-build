import { useMemo } from "react";
import { Pressable, Text, View } from "react-native";
import { EmptyState, ErrorState, Field, LoadingState, MetricCard, Panel, Pill, Row, Screen, SectionHeader, colors, toneForStatus } from "@/components/ui";
import { useSession } from "@/auth/SessionProvider";
import { useWorkflow } from "@/workflow/WorkflowContext";
import { useAsyncResource } from "@/hooks/useAsyncResource";
import { asRecords, textOf } from "@/data/records";

export function DashboardScreen() {
  const { session, roleModel, api, hasPermission } = useSession();
  const { selectedJobId, setSelectedJobId } = useWorkflow();
  const canReadJobs = ["jobs:view", "shipments:view", "dispatch:view", "dispatch:manage"].some(hasPermission);
  const jobs = useAsyncResource(async () => canReadJobs ? api.jobs() : [], [api, canReadJobs]);
  const recentJobs = useMemo(() => asRecords(jobs.data), [jobs.data]);

  return (
    <Screen>
      <Panel>
        <SectionHeader
          eyebrow={session?.company.name}
          title={`Hello, ${session?.user.name ?? "operator"}`}
          description={roleModel.subtitle}
          right={<Pill label={roleModel.title} tone="teal" />}
        />
        <Row>
          <MetricCard label="Workspace" value={session?.company.code ?? "Unknown"} tone="teal" />
          <MetricCard label="Open work" value={canReadJobs ? String(recentJobs.length) : "Scoped"} tone="blue" />
          <MetricCard label="Selected" value={selectedJobId ? `#${selectedJobId}` : "None"} tone="amber" />
        </Row>
      </Panel>

      <Panel>
        <SectionHeader eyebrow="Operational inbox" title="Recent authorized work" description="Choose only from records returned by your session. Manual database-ID entry is intentionally unavailable." />
        {jobs.loading ? <LoadingState label="Loading authorized work…" /> : null}
        {jobs.error ? <ErrorState title="Work unavailable" body={jobs.error} onRetry={jobs.refresh} /> : null}
        {!canReadJobs ? <EmptyState title="No operations feed for this role" body="Your mobile workspace only exposes records and actions granted by the backend session." /> : null}
        {canReadJobs && !jobs.loading && !jobs.error && recentJobs.length === 0 ? <EmptyState title="No open work" body="The backend returned no current records. OpsTrax does not create placeholder jobs." /> : null}
        <View style={{ gap: 10 }}>
          {recentJobs.slice(0, 20).map((job, index) => {
            const id = Number(job.id);
            const selected = Number.isFinite(id) && selectedJobId === id;
            return (
              <Pressable
                key={String(job.id ?? index)}
                accessibilityRole="button"
                accessibilityLabel={`Select ${textOf(job.jobNumber ?? job.jobCode ?? job.reference, `work item ${index + 1}`)}`}
                onPress={() => Number.isFinite(id) && id > 0 ? setSelectedJobId(id) : undefined}
                style={({ pressed }) => [{
                  borderRadius: 18,
                  borderWidth: 1,
                  borderColor: selected ? colors.teal + "88" : colors.border,
                  backgroundColor: selected ? colors.teal + "12" : colors.panelAlt,
                  padding: 14,
                  gap: 9,
                  opacity: pressed ? 0.82 : 1,
                }]}
              >
                <Row>
                  <View style={{ flex: 1, minWidth: 180, gap: 4 }}>
                    <Text style={{ color: colors.text, fontSize: 15, fontWeight: "900" }}>{textOf(job.jobNumber ?? job.jobCode ?? job.reference, `Work item ${index + 1}`)}</Text>
                    <Text style={{ color: colors.muted, fontSize: 13, lineHeight: 18 }}>{textOf(job.customerName ?? job.title ?? job.description)}</Text>
                  </View>
                  <Pill label={textOf(job.status, "Open")} tone={toneForStatus(String(job.status ?? ""))} />
                </Row>
                <Field label="Schedule" value={textOf(job.scheduledStart ?? job.createdAt)} />
              </Pressable>
            );
          })}
        </View>
      </Panel>

      <Panel>
        <SectionHeader eyebrow="Tenant boundary" title="One active organization" description="The authenticated server session binds this app to one tenant, user, branch, role, and permission set." />
        <Field label="Organization" value={session?.company.name} />
        <Field label="Organization code" value={session?.company.code} />
        <Field label="Signed-in user" value={session?.user.email} />
      </Panel>
    </Screen>
  );
}
