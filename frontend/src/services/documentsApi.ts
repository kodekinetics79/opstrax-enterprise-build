import { apiClient, unwrap } from "@/services/apiClient";
import { sessionBoundRequest } from "@/auth/requestSessionGuard";
import type { AnyRecord, UserSession } from "@/types";

export const documentsApi = {
  list: (session: UserSession) => unwrap<AnyRecord[]>(apiClient.get("/api/documents", sessionBoundRequest(session))),
  summary: (session: UserSession) => unwrap<AnyRecord>(apiClient.get("/api/documents/summary", sessionBoundRequest(session))),
  detail: (id: string | number, session: UserSession) => unwrap<AnyRecord>(apiClient.get(`/api/documents/${id}`, sessionBoundRequest(session))),
  create: (payload: AnyRecord, session: UserSession) => unwrap<AnyRecord>(apiClient.post("/api/documents", payload, sessionBoundRequest(session))),
  upload: (payload: AnyRecord, session: UserSession) => {
    const form = new FormData();
    const file = payload.file;
    if (!(file instanceof File)) throw new Error("Choose a document file to upload.");
    form.append("file", file);
    Object.entries(payload).forEach(([key, value]) => {
      if (key !== "file" && value !== undefined && value !== null && String(value).trim() !== "") {
        form.append(key, String(value));
      }
    });
    return unwrap<AnyRecord>(apiClient.post("/api/documents/upload", form, sessionBoundRequest(session, {
      headers: { "Content-Type": "multipart/form-data" },
      timeout: 120_000,
    })));
  },
  update: (id: string | number, payload: AnyRecord, session: UserSession) => unwrap<AnyRecord>(apiClient.put(`/api/documents/${id}`, payload, sessionBoundRequest(session))),
  remove: (id: string | number, session: UserSession) => unwrap<AnyRecord>(apiClient.delete(`/api/documents/${id}`, sessionBoundRequest(session))),
  expiring: (session: UserSession) => unwrap<AnyRecord[]>(apiClient.get("/api/documents/expiring", sessionBoundRequest(session))),
  renew: (id: string | number, expectedVersion: string, session: UserSession) => unwrap<AnyRecord>(apiClient.post(`/api/documents/${id}/renew`, { expectedVersion }, sessionBoundRequest(session))),
  timeline: (id: string | number, session: UserSession) => unwrap<AnyRecord[]>(apiClient.get(`/api/documents/${id}/timeline`, sessionBoundRequest(session))),
  recommendations: (session: UserSession) => unwrap<AnyRecord[]>(apiClient.get("/api/documents/recommendations", sessionBoundRequest(session))),
};
