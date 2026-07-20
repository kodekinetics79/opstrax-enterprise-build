import { useMemo } from "react";
import { Pressable, Text, View } from "react-native";
import { ActionButton, EmptyState, Field, Input, LoadingState, MetricCard, Panel, Pill, Row, Screen, SectionHeader, colors } from "@/components/ui";
import { useSession } from "@/auth/SessionProvider";
import { useWorkflow } from "@/workflow/WorkflowContext";
import { useAsyncResource } from "@/hooks/useAsyncResource";
import { ROLE_MODELS } from "@/data/roleModel";
import type { JsonRecord } from "@/types";

function asArray(value: unknown): JsonRecord[] {
  if (Array.isArray(value)) return value as JsonRecord[];
  if (value && typeof value === "object") {
    const record = value as Record<string, unknown>;
    for (const key of ["items", "rows", "data", "results"]) {
      if (Array.isArray(record[key])) return record[key] as JsonRecord[];
    }
  }
  return [];
}

function textOf(value: unknown) {
  return value === null || value === undefined || value === "" ? "No data yet" : String(value);
}

export function DashboardScreen() {
  const { session, roleModel, api, hasPermission } = useSession();
  const { selectedJobId, selectedJobInput, setSelectedJobInput, applySelectedJob, setSelectedJobId } = useWorkflow();
  const jobs = useAsyncResource(async () => api.jobs(), [api]);
  const summary = useAsyncResource(async () => (selectedJobId ? api.executionSummary(selectedJobId) : null), [api, selectedJobId]);

  const recentJobs = useMemo(() => asArray(jobs.data), [jobs.data]);
  const previewSummary = summary.data ?? null;
  const routeModel = ROLE_MODELS.find((entry) => entry.role === roleModel.role) ?? roleModel;

  return (
    <Screen>
      <Panel>
        <SectionHeader
          eyebrow="Mobile foundation"
          title={`Hello, ${session?.user.name ?? "operator"}`}
          description="This dashboard is role-aware, tenant-scoped, and driven by the live backend session."
          right={<Pill label={routeModel.title} tone="teal" />}
        />
        <Row>
          <MetricCard label="Permissions" value={String(session?.permissions?.length ?? 0)} tone="blue" />
          <MetricCard label="Role family" value={routeModel.title} tone="teal" />
          <MetricCard label="Job selected" value={selectedJobId ? String(selectedJobId) : "None"} tone="amber" />
        </Row>
      </Panel>

      <Panel>
        <SectionHeader
          eyebrow="Route model"
          title="What this session can reach"
          description="The mobile app uses the backend session permissions, not a separate mobile authorization path."
        />
        <View style={{ gap: 10 }}>
          {routeModel.routeFamilies.map((family) => (
            <Field key={family} label="Route family" value={family} />
          ))}
        </View>
        <View style={{ gap: 10 }}>
          <Text style={{ color: colors.muted, fontSize: 12, fontWeight: "700", textTransform: "uppercase", letterSpacing: 1.2 }}>Hidden data by design</Text>
          {routeModel.hiddenData.map((item) => (
            <Text key={item} style={{ color: colors.text, lineHeight: 19 }}>
              • {item}
            </Text>
          ))}
        </View>
      </Panel>

      <Panel>
        <SectionHeader
          eyebrow="Job context"
          title="Load a live execution summary"
          description="The workflows tab uses the real job id. No fake job data is injected here."
        />
        <View style={{ gap: 10 }}>
          <Field label="Selected job" value={selectedJobId ? String(selectedJobId) : null} />
          <Field label="Backend execution summary access" value={hasPermission("operations.execution_summary.read") ? "Allowed" : "Denied"} />
          <Input label="Job id input" value={selectedJobInput} onChangeText={setSelectedJobInput} placeholder="Enter a real job id" keyboardType="numeric" />
          <ActionButton label="Set job" onPress={applySelectedJob} />
          <ActionButton label="Clear job" onPress={() => setSelectedJobId(null)} variant="secondary" />
        </View>
      </Panel>

      <Panel>
        <SectionHeader eyebrow="Recent jobs" title="Real backend job list" description="This is a live list from the existing job endpoint when the backend is reachable." />
        {jobs.loading ? <LoadingState label="Loading jobs..." /> : jobs.error ? <EmptyState title="Jobs unavailable" body={jobs.error} /> : null}
        {!jobs.loading && !jobs.error && recentJobs.length === 0 ? (
          <EmptyState title="No jobs yet" body="The backend returned an empty job list. That is honest; the app does not fabricate assignments." />
        ) : null}
        <View style={{ gap: 10 }}>
          {recentJobs.slice(0, 5).map((job, index) => (
            <Pressable key={String(job.id ?? index)} onPress={() => setSelectedJobId(Number(job.id ?? 0) || null)} style={({ pressed }) => [{ opacity: pressed ? 0.85 : 1 }, styles.jobRow]}>
              <View style={{ flex: 1, gap: 4 }}>
                <Text style={styles.jobTitle}>{textOf(job.jobNumber ?? job.job_number ?? job.reference ?? `Job ${job.id ?? index + 1}`)}</Text>
                <Text style={styles.jobSubtitle}>{textOf(job.customerName ?? job.customer_name ?? job.title ?? job.description)}</Text>
              </View>
              <View style={{ alignItems: "flex-end", gap: 6 }}>
                <Pill label={textOf(job.status ?? "Open")} tone="teal" />
                <Text style={styles.jobMeta}>{textOf(job.scheduledStart ?? job.scheduled_start ?? job.createdAt ?? job.created_at)}</Text>
              </View>
            </Pressable>
          ))}
        </View>
      </Panel>

      <Panel>
        <SectionHeader eyebrow="Execution summary" title="Live workflow snapshot" description="The summary below is pulled from the real backend once a job id is selected." />
        {selectedJobId == null ? (
          <EmptyState title="No job selected" body="Set a live job id above to view assignment, proof, access, and billing confidence summaries." />
        ) : summary.loading ? (
          <LoadingState label="Loading execution summary..." />
        ) : summary.error ? (
          <EmptyState title="Summary unavailable" body={summary.error} />
        ) : previewSummary ? (
          <View style={{ gap: 10 }}>
            <Field label="Execution status" value={textOf((previewSummary as JsonRecord).status ?? (previewSummary as JsonRecord).summary_status)} />
            <Field label="Assignment summary" value={textOf((previewSummary as JsonRecord).assignment_summary ?? (previewSummary as JsonRecord).smart_assignment_summary)} />
            <Field label="Site access summary" value={textOf((previewSummary as JsonRecord).site_access_summary)} />
            <Field label="Proof package summary" value={textOf((previewSummary as JsonRecord).proof_package_summary)} />
            <Field label="Billing confidence summary" value={textOf((previewSummary as JsonRecord).billing_confidence_summary)} />
          </View>
        ) : (
          <EmptyState title="No summary data" body="The backend returned no execution summary object for this job yet." />
        )}
      </Panel>
    </Screen>
  );
}

const styles = {
  jobRow: {
    borderRadius: 18,
    borderWidth: 1,
    borderColor: colors.border,
    backgroundColor: colors.panelAlt,
    padding: 14,
    flexDirection: "row" as const,
    alignItems: "center" as const,
    justifyContent: "space-between" as const,
    gap: 12,
  },
  jobTitle: {
    color: colors.text,
    fontSize: 15,
    fontWeight: "800" as const,
  },
  jobSubtitle: {
    color: colors.muted,
    fontSize: 13,
    lineHeight: 18,
  },
  jobMeta: {
    color: colors.subtle,
    fontSize: 11,
    fontWeight: "700" as const,
  },
};
