import { apiClient, unwrap } from "./apiClient";
import type { AnyRecord } from "@/types";

// P6 Mobile Driver Workflow — all endpoints derive driver identity from auth session.
// Never send driverId in request body as authoritative.

export const driverApi = {
  // ── Identity + dashboard ───────────────────────────────────────────────────
  me: () =>
    unwrap<AnyRecord>(apiClient.get("/api/driver/me")),

  // ── Assignments ────────────────────────────────────────────────────────────
  assignments: () =>
    unwrap<AnyRecord[]>(apiClient.get("/api/driver/assignments")),

  currentAssignment: () =>
    unwrap<AnyRecord>(apiClient.get("/api/driver/assignments/current")),

  acceptAssignment: (id: number | string) =>
    unwrap<AnyRecord>(apiClient.post(`/api/driver/assignments/${id}/accept`, {})),

  updateStatus: (id: number | string, status: string, notes?: string) =>
    unwrap<AnyRecord>(
      apiClient.post(`/api/driver/assignments/${id}/status`, { status, notes })
    ),

  reportException: (
    id: number | string,
    payload: {
      exceptionType: string;
      severity?: string;
      title?: string;
      notes?: string;
    }
  ) =>
    unwrap<AnyRecord>(
      apiClient.post(`/api/driver/assignments/${id}/exception`, payload)
    ),

  submitProof: (
    id: number | string,
    payload: {
      proofType: "pickup" | "delivery";
      notes?: string;
      evidenceHash?: string;
      lat?: number;
      lng?: number;
      // POD media captured on the device. These MUST travel in their own field — they used
      // to be JSON-packed into evidenceHash, which is a VARCHAR(128) on the server, so any
      // POD with a photo attached blew the column and 500'd. A driver could not confirm a
      // delivery with proof. Do not fold these back into evidenceHash.
      artifacts?: Array<{
        kind: "photo" | "signature";
        reference: string;
        contentType?: string;
        size?: number;
      }>;
    }
  ) =>
    unwrap<AnyRecord>(
      apiClient.post(`/api/driver/assignments/${id}/proof`, payload)
    ),

  // Upload a POD photo or signature to durable storage; returns { reference, url, kind }.
  uploadProofArtifact: (id: number | string, file: Blob, kind: string, filename: string) => {
    const fd = new FormData();
    fd.append("file", file, filename);
    fd.append("kind", kind);
    return unwrap<AnyRecord>(apiClient.post(`/api/driver/assignments/${id}/proof/upload`, fd));
  },

  // ── DVIR ─────────────────────────────────────────────────────────────────
  dvirTemplates: () =>
    unwrap<AnyRecord[]>(apiClient.get("/api/driver/dvir/templates")),

  submitDvir: (payload: {
    vehicleId: number;
    driverId?: number;   // backend overrides with session identity; send 0 or omit
    inspectionType?: string;
    odometerMiles?: number;
    engineHours?: number;
    notes?: string;
    checklistItems?: {
      category?: string;
      itemName?: string;
      result: "pass" | "fail" | "na";
      severity?: string;
      notes?: string;
    }[];
  }) =>
    unwrap<AnyRecord>(apiClient.post("/api/driver/dvir", { ...payload, driverId: payload.driverId ?? 0 })),

  // ── Coaching ───────────────────────────────────────────────────────────────
  coaching: () =>
    unwrap<AnyRecord>(apiClient.get("/api/driver/coaching")),

  acknowledgeCoaching: (id: number | string, note?: string) =>
    unwrap<AnyRecord>(
      apiClient.post(`/api/driver/coaching/${id}/acknowledge`, { note })
    ),

  // ── HOS/ELD ───────────────────────────────────────────────────────────────
  hos: () =>
    unwrap<AnyRecord>(apiClient.get("/api/driver/hos")),
};
