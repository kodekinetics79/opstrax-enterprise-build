import { useMemo, useState } from "react";
import { Alert, Text, View } from "react-native";
import * as Haptics from "expo-haptics";
import { useSession } from "@/auth/SessionProvider";
import { useAsyncResource } from "@/hooks/useAsyncResource";
import { asRecords, textOf, titleCase } from "@/data/records";
import type { JsonRecord } from "@/types";
import {
  ActionButton,
  EmptyState,
  ErrorState,
  Field,
  HeroPanel,
  Input,
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

const FEEDBACK_TYPES = ["support", "delivery", "claim", "billing", "general"] as const;

export function CustomerSupportScreen() {
  const { api } = useSession();
  const [selectedJobId, setSelectedJobId] = useState<number | null>(null);
  const [feedbackType, setFeedbackType] = useState<(typeof FEEDBACK_TYPES)[number]>("support");
  const [subject, setSubject] = useState("");
  const [comment, setComment] = useState("");
  const [rating, setRating] = useState<number | null>(null);
  const [busy, setBusy] = useState(false);

  const jobs = useAsyncResource(
    async () => (await api.request.get<{ items?: JsonRecord[] }>("/api/portal/jobs")).items ?? [],
    [api],
  );
  const feedback = useAsyncResource(
    async () => (await api.request.get<{ items?: JsonRecord[] }>("/api/portal/feedback")).items ?? [],
    [api],
  );

  const jobRows = useMemo(() => asRecords(jobs.data), [jobs.data]);
  const feedbackRows = useMemo(() => asRecords(feedback.data), [feedback.data]);
  const openCount = useMemo(() => feedbackRows.filter((item) => !/closed|resolved/i.test(String(item.status ?? ""))).length, [feedbackRows]);

  const submit = async () => {
    if (!selectedJobId || !subject.trim() || !comment.trim()) return;
    setBusy(true);
    try {
      await api.request.post<JsonRecord>("/api/portal/feedback", {
        jobId: selectedJobId,
        feedbackType,
        subject: subject.trim(),
        comment: comment.trim(),
        rating,
      });
      await Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      setSubject("");
      setComment("");
      setRating(null);
      await feedback.refresh();
      Alert.alert("Request submitted", "Your request is now recorded against this shipment and is visible to your service team.");
    } catch (error) {
      Alert.alert("Request not submitted", error instanceof Error ? error.message : "The server rejected the request.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <Screen>
      <HeroPanel tone="violet">
        <SectionHeader
          eyebrow="Customer care"
          title="Support & feedback"
          description="Open a shipment-specific request without calling dispatch. Every request is tied to a shipment your account owns."
          right={<Pill label="Customer scoped" tone="green" />}
        />
        <Row>
          <MetricCard label="Open requests" value={feedback.loading ? "…" : String(openCount)} helper="Waiting or in progress" tone={openCount > 0 ? "amber" : "green"} />
          <MetricCard label="History" value={feedback.loading ? "…" : String(feedbackRows.length)} helper="Your requests only" tone="blue" />
        </Row>
      </HeroPanel>

      <Panel variant="elevated" tone="blue">
        <SectionHeader
          eyebrow="New request"
          title="Tell us what you need"
          description="Choose one of your shipments. The backend rejects requests for any shipment outside your customer account."
        />

        {jobs.loading ? <LoadingState label="Loading your shipments…" /> : null}
        {jobs.error ? <ErrorState title="Shipments unavailable" body={jobs.error} onRetry={jobs.refresh} /> : null}
        {!jobs.loading && !jobs.error && jobRows.length === 0 ? (
          <EmptyState title="No shipment available" body="A support request must be tied to one of your shipments." />
        ) : null}

        {jobRows.length ? (
          <View style={{ gap: 10 }}>
            <Text style={{ color: colors.subtle, fontSize: 10, fontWeight: "800", textTransform: "uppercase", letterSpacing: 1 }}>Shipment</Text>
            <View style={{ gap: 8 }}>
              {jobRows.slice(0, 12).map((job, index) => {
                const id = Number(job.id);
                const selected = Number.isFinite(id) && selectedJobId === id;
                return (
                  <ActionButton
                    key={String(job.id ?? index)}
                    label={`${selected ? "✓ " : ""}${textOf(job.jobNumber ?? job.trackingCode, `Shipment ${index + 1}`)} · ${textOf(job.status, "Pending")}`}
                    onPress={() => Number.isFinite(id) && id > 0 ? setSelectedJobId(id) : undefined}
                    variant={selected ? "secondary" : "ghost"}
                    disabled={!Number.isFinite(id) || id <= 0}
                  />
                );
              })}
            </View>
          </View>
        ) : null}

        <View style={{ gap: 10 }}>
          <Text style={{ color: colors.subtle, fontSize: 10, fontWeight: "800", textTransform: "uppercase", letterSpacing: 1 }}>Request type</Text>
          <Row>
            {FEEDBACK_TYPES.map((type) => (
              <ActionButton
                key={type}
                label={titleCase(type)}
                onPress={() => setFeedbackType(type)}
                variant={feedbackType === type ? "secondary" : "ghost"}
              />
            ))}
          </Row>
        </View>

        <Input label="Subject" value={subject} onChangeText={setSubject} placeholder="What can we help with?" autoCapitalize="sentences" />
        <Input label="Details" value={comment} onChangeText={setComment} placeholder="Describe the issue, question, claim, or request." multiline autoCapitalize="sentences" />

        <View style={{ gap: 10 }}>
          <Text style={{ color: colors.subtle, fontSize: 10, fontWeight: "800", textTransform: "uppercase", letterSpacing: 1 }}>Optional delivery rating</Text>
          <Row>
            {[1, 2, 3, 4, 5].map((value) => (
              <ActionButton key={value} label={`${rating === value ? "★" : "☆"} ${value}`} onPress={() => setRating(rating === value ? null : value)} variant={rating === value ? "secondary" : "ghost"} />
            ))}
          </Row>
        </View>

        <ActionButton
          label={busy ? "Submitting…" : "Submit request"}
          onPress={() => void submit()}
          disabled={busy || !selectedJobId || subject.trim().length < 3 || comment.trim().length < 5}
        />
      </Panel>

      <Panel variant="elevated" tone="violet">
        <SectionHeader eyebrow="Request history" title="Your support activity" description="Only feedback records belonging to your customer account are returned." />
        {feedback.loading ? <LoadingState label="Loading support history…" /> : null}
        {feedback.error ? <ErrorState title="Support history unavailable" body={feedback.error} onRetry={feedback.refresh} /> : null}
        {!feedback.loading && !feedback.error && feedbackRows.length === 0 ? (
          <EmptyState title="No support requests yet" body="Your submitted requests will appear here with their current status." />
        ) : null}
        <View style={{ gap: 12 }}>
          {feedbackRows.slice(0, 20).map((item, index) => (
            <Panel key={String(item.id ?? index)} variant="quiet" tone={toneForStatus(String(item.status ?? ""))}>
              <Row>
                <View style={{ flex: 1, gap: 4 }}>
                  <Text style={{ color: colors.text, fontSize: 15, fontWeight: "900" }}>{textOf(item.subject, "Customer request")}</Text>
                  <Text style={{ color: colors.muted, fontSize: 12.5, lineHeight: 18 }}>{textOf(item.comment)}</Text>
                </View>
                <Pill label={textOf(item.status, "Open")} tone={toneForStatus(String(item.status ?? ""))} />
              </Row>
              <Row>
                <View style={{ flex: 1 }}><Field label="Type" value={titleCase(textOf(item.feedbackType ?? item.feedback_type, "general"))} /></View>
                <View style={{ flex: 1 }}><Field label="Shipment" value={textOf(item.jobId ?? item.job_id)} /></View>
              </Row>
              <Field label="Created" value={textOf(item.createdAt ?? item.created_at)} />
            </Panel>
          ))}
        </View>
      </Panel>
    </Screen>
  );
}
