import { useEffect, useState } from "react";
import { Alert, Image, Text } from "react-native";
import * as Haptics from "expo-haptics";
import * as ImagePicker from "expo-image-picker";
import * as Location from "expo-location";
import {
  ActionButton,
  EmptyState,
  ErrorState,
  Field,
  HeroPanel,
  Input,
  LoadingState,
  Panel,
  Pill,
  Row,
  Screen,
  SectionHeader,
  colors,
} from "@/components/ui";
import { useSession } from "@/auth/SessionProvider";
import { useAsyncResource } from "@/hooks/useAsyncResource";
import { clearSecureDraft, readSecureDraft, secureDraftKey, writeSecureDraft } from "@/storage/secureDrafts";
import type { DriverProofArtifact } from "@/types";
import { textOf, titleCase } from "@/data/records";

type CapturedAsset = {
  uri: string;
  fileName?: string | null;
  mimeType?: string | null;
  fileSize?: number;
  file?: Blob | null;
};

type ProofDraft = {
  notes: string;
  uploaded: DriverProofArtifact | null;
};

async function captureOptionalCoordinates(): Promise<{ lat: number; lng: number } | null> {
  try {
    const permission = await Location.requestForegroundPermissionsAsync();
    if (!permission.granted) return null;
    const position = await Location.getCurrentPositionAsync({ accuracy: Location.Accuracy.High });
    const lat = position.coords.latitude;
    const lng = position.coords.longitude;
    return Number.isFinite(lat) && Number.isFinite(lng) ? { lat, lng } : null;
  } catch {
    return null;
  }
}

export function DriverProofScreen() {
  const { api, session } = useSession();
  const current = useAsyncResource(() => api.driverCurrentAssignment(), [api]);
  const [captured, setCaptured] = useState<CapturedAsset | null>(null);
  const [uploaded, setUploaded] = useState<DriverProofArtifact | null>(null);
  const [notes, setNotes] = useState("");
  const [draftReady, setDraftReady] = useState(false);
  const [busy, setBusy] = useState(false);
  const assignment = current.data?.assignment;
  const status = String(assignment?.assignmentStatus ?? "").toLowerCase();
  const proofType = status === "arrived_delivery" ? "delivery" : "pickup";
  const canSubmit = Boolean(assignment?.id && (status === "arrived_delivery" || status === "arrived_pickup" || status === "loaded"));
  const proofDraftKey = assignment?.id
    ? secureDraftKey("driver-proof", session?.company.id ?? session?.company.code, session?.user.id, assignment.id)
    : null;

  useEffect(() => {
    let active = true;
    setDraftReady(false);
    setCaptured(null);
    if (!proofDraftKey) return () => { active = false; };
    void readSecureDraft<ProofDraft>(proofDraftKey).then((draft) => {
      if (!active) return;
      if (draft) {
        setNotes(draft.notes ?? "");
        setUploaded(draft.uploaded?.reference ? draft.uploaded : null);
      } else {
        setNotes("");
        setUploaded(null);
      }
      setDraftReady(true);
    });
    return () => { active = false; };
  }, [proofDraftKey]);

  useEffect(() => {
    if (!proofDraftKey || !draftReady) return;
    const timer = setTimeout(() => {
      const hasDraft = Boolean(notes.trim() || uploaded?.reference);
      const write = hasDraft
        ? writeSecureDraft<ProofDraft>(proofDraftKey, { notes, uploaded })
        : clearSecureDraft(proofDraftKey);
      void write.catch(() => undefined);
    }, 250);
    return () => clearTimeout(timer);
  }, [draftReady, notes, proofDraftKey, uploaded]);

  const capture = async () => {
    const permission = await ImagePicker.requestCameraPermissionsAsync();
    if (!permission.granted) {
      Alert.alert("Camera permission needed", "Use device settings to allow the camera, or complete proof from another authorized device.");
      return;
    }
    const result = await ImagePicker.launchCameraAsync({ mediaTypes: ["images"], quality: 0.75, cameraType: ImagePicker.CameraType.back });
    if (result.canceled) return;
    const asset = result.assets[0];
    setCaptured({ uri: asset.uri, fileName: asset.fileName, mimeType: asset.mimeType, fileSize: asset.fileSize, file: asset.file });
    setUploaded(null);
  };

  const upload = async () => {
    if (!assignment?.id || !captured) return;
    setBusy(true);
    try {
      const artifact = await api.uploadDriverProofArtifact(assignment.id, captured, "photo");
      if (!artifact.reference) throw new Error("The upload completed without a durable evidence reference.");
      setUploaded(artifact);
      await Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
    } catch (error) {
      Alert.alert("Evidence upload failed", error instanceof Error ? error.message : "The server rejected the upload.");
    } finally {
      setBusy(false);
    }
  };

  const submit = async () => {
    if (!assignment?.id || !uploaded) return;
    setBusy(true);
    try {
      const coordinates = await captureOptionalCoordinates();
      await api.submitDriverProof(assignment.id, {
        proofType,
        notes: notes.trim() || undefined,
        ...(coordinates ?? {}),
        artifacts: [{
          kind: uploaded.kind,
          reference: uploaded.reference,
          contentType: uploaded.contentType,
          size: uploaded.size,
        }],
      });
      await Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      if (proofDraftKey) await clearSecureDraft(proofDraftKey).catch(() => undefined);
      setCaptured(null);
      setUploaded(null);
      setNotes("");
      current.refresh();
      Alert.alert("Proof recorded", proofType === "delivery" ? "Delivery is complete and dispatch has been updated." : "Pickup evidence is now attached to this load.");
    } catch (error) {
      Alert.alert("Proof submission failed", error instanceof Error ? `${error.message} Uploaded evidence and notes remain saved on this device.` : "The server rejected the proof. Your draft remains saved.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <Screen>
      <HeroPanel tone={canSubmit ? "teal" : "amber"}>
        <SectionHeader
          eyebrow="Evidence"
          title="Proof of pickup or delivery"
          description="Evidence is uploaded to tenant-scoped storage first, then attached to the authorized assignment."
          right={assignment ? <Pill label={titleCase(status)} tone={canSubmit ? "teal" : "amber"} /> : undefined}
        />
      </HeroPanel>

      {current.loading ? <LoadingState label="Checking assignment state…" /> : null}
      {current.error ? <ErrorState title="Proof unavailable" body={current.error} /> : null}
      {!current.loading && !assignment ? <EmptyState title="No active assignment" body="Proof capture unlocks when a load is assigned to your driver identity." /> : null}

      {assignment ? (
        <>
          <Panel variant="quiet" tone={canSubmit ? "teal" : "amber"}>
            <SectionHeader
              eyebrow="Load"
              title={textOf(assignment.shipmentNumber)}
              description={canSubmit ? `${titleCase(proofType)} proof is allowed at this assignment state.` : "Progress the trip to a pickup or delivery arrival state before submitting proof."}
            />
            <Field label="Vehicle" value={assignment.vehicleCode} />
            <Field label="Destination" value={proofType === "delivery" ? assignment.dropoffAddress : assignment.pickupAddress} />
          </Panel>

          <Panel variant="elevated" tone="blue">
            <SectionHeader
              eyebrow="Capture"
              title="Take a live photo"
              description="Camera access is requested only when you choose to capture evidence."
            />
            {captured ? (
              <Image
                source={{ uri: captured.uri }}
                style={{ width: "100%", height: 230, borderRadius: 22, borderWidth: 1, borderColor: colors.border }}
                resizeMode="cover"
              />
            ) : null}
            <Row>
              <ActionButton label={captured ? "Retake photo" : "Take photo"} onPress={() => void capture()} variant="secondary" disabled={!canSubmit || busy} />
              {captured && !uploaded ? <ActionButton label={busy ? "Uploading…" : "Upload evidence"} onPress={() => void upload()} disabled={busy} /> : null}
            </Row>
            {captured && !uploaded ? (
              <Text style={{ color: colors.amber, fontSize: 12, lineHeight: 18 }}>
                This photo is still local to the current app session. Upload it before closing the app so the evidence receives a durable server reference.
              </Text>
            ) : null}
            {uploaded ? <Pill label={captured ? "Evidence securely uploaded" : "Uploaded evidence recovered"} tone="green" /> : null}
          </Panel>

          <Panel variant="elevated" tone="teal">
            <SectionHeader
              eyebrow="Submit"
              title={`Record ${proofType} proof`}
              description="Foreground location is requested at submission and included only when permission is granted. Notes and uploaded evidence references are encrypted on-device until submission succeeds."
            />
            <Input label="Delivery notes" value={notes} onChangeText={setNotes} placeholder="Receiver, condition, or exception notes" multiline autoCapitalize="sentences" />
            {notes.trim() || uploaded ? <Pill label="Draft saved securely" tone="blue" /> : null}
            <ActionButton label={busy ? "Submitting…" : `Submit ${proofType} proof`} onPress={() => void submit()} disabled={!uploaded || !canSubmit || busy} />
          </Panel>
        </>
      ) : null}
    </Screen>
  );
}
