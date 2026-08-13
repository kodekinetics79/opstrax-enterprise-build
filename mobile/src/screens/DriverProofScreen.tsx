import { useState } from "react";
import { Alert, Image } from "react-native";
import * as Haptics from "expo-haptics";
import * as ImagePicker from "expo-image-picker";
import * as Location from "expo-location";
import { ActionButton, EmptyState, ErrorState, Field, Input, LoadingState, Panel, Pill, Row, Screen, SectionHeader } from "@/components/ui";
import { useSession } from "@/auth/SessionProvider";
import { useAsyncResource } from "@/hooks/useAsyncResource";
import type { DriverProofArtifact } from "@/types";
import { textOf, titleCase } from "@/data/records";

type CapturedAsset = {
  uri: string;
  fileName?: string | null;
  mimeType?: string | null;
  fileSize?: number;
  file?: Blob | null;
};

export function DriverProofScreen() {
  const { api } = useSession();
  const current = useAsyncResource(() => api.driverCurrentAssignment(), [api]);
  const [captured, setCaptured] = useState<CapturedAsset | null>(null);
  const [uploaded, setUploaded] = useState<DriverProofArtifact | null>(null);
  const [notes, setNotes] = useState("");
  const [busy, setBusy] = useState(false);
  const assignment = current.data?.assignment;
  const status = String(assignment?.assignmentStatus ?? "").toLowerCase();
  const proofType = status === "arrived_delivery" ? "delivery" : "pickup";
  const canSubmit = Boolean(assignment?.id && (status === "arrived_delivery" || status === "arrived_pickup" || status === "loaded"));

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
      let coords: { lat?: number; lng?: number } = {};
      const permission = await Location.requestForegroundPermissionsAsync();
      if (permission.granted) {
        const position = await Location.getCurrentPositionAsync({ accuracy: Location.Accuracy.High });
        coords = { lat: position.coords.latitude, lng: position.coords.longitude };
      }
      await api.submitDriverProof(assignment.id, {
        proofType,
        notes: notes.trim() || undefined,
        ...coords,
        artifacts: [{
          kind: uploaded.kind,
          reference: uploaded.reference,
          contentType: uploaded.contentType,
          size: uploaded.size,
        }],
      });
      await Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
      setCaptured(null);
      setUploaded(null);
      setNotes("");
      current.refresh();
      Alert.alert("Proof recorded", proofType === "delivery" ? "Delivery is complete and dispatch has been updated." : "Pickup evidence is now attached to this load.");
    } catch (error) {
      Alert.alert("Proof submission failed", error instanceof Error ? error.message : "The server rejected the proof.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <Screen>
      <Panel>
        <SectionHeader eyebrow="Evidence" title="Proof of pickup or delivery" description="Evidence uploads first to tenant-scoped object storage. The returned reference is then attached to the assignment." right={assignment ? <Pill label={titleCase(status)} tone={canSubmit ? "teal" : "amber"} /> : undefined} />
      </Panel>
      {current.loading ? <LoadingState label="Checking assignment state…" /> : null}
      {current.error ? <ErrorState title="Proof unavailable" body={current.error} /> : null}
      {!current.loading && !assignment ? <EmptyState title="No active assignment" body="Proof capture unlocks when a load is assigned to your driver identity." /> : null}
      {assignment ? (
        <>
          <Panel>
            <SectionHeader eyebrow="Load" title={textOf(assignment.shipmentNumber)} description={canSubmit ? `${titleCase(proofType)} proof is allowed at this assignment state.` : "Progress the trip to a pickup or delivery arrival state before submitting proof."} />
            <Field label="Vehicle" value={assignment.vehicleCode} />
            <Field label="Destination" value={proofType === "delivery" ? assignment.dropoffAddress : assignment.pickupAddress} />
          </Panel>

          <Panel>
            <SectionHeader eyebrow="Capture" title="Take a live photo" description="Camera access is requested only when you choose to capture evidence." />
            {captured ? <Image source={{ uri: captured.uri }} style={{ width: "100%", height: 220, borderRadius: 18 }} resizeMode="cover" /> : null}
            <Row>
              <ActionButton label={captured ? "Retake photo" : "Take photo"} onPress={() => void capture()} variant="secondary" disabled={!canSubmit || busy} />
              {captured && !uploaded ? <ActionButton label={busy ? "Uploading…" : "Upload evidence"} onPress={() => void upload()} disabled={busy} /> : null}
            </Row>
            {uploaded ? <Pill label="Evidence securely uploaded" tone="green" /> : null}
          </Panel>

          <Panel>
            <SectionHeader eyebrow="Submit" title={`Record ${proofType} proof`} description="Foreground location is requested at submission and included only when permission is granted." />
            <Input label="Delivery notes" value={notes} onChangeText={setNotes} placeholder="Receiver, condition, or exception notes" multiline autoCapitalize="sentences" />
            <ActionButton label={busy ? "Submitting…" : `Submit ${proofType} proof`} onPress={() => void submit()} disabled={!uploaded || !canSubmit || busy} />
          </Panel>
        </>
      ) : null}
    </Screen>
  );
}
