import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";

export const documentsApi = {
  list: () => unwrap<AnyRecord[]>(apiClient.get("/api/documents")),
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/documents/summary")),
  detail: (id: string | number) => unwrap<AnyRecord>(apiClient.get(`/api/documents/${id}`)),
  create: (payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/documents", payload)),
  upload: (payload: AnyRecord) => {
    const form = new FormData();
    const file = payload.file;
    if (!(file instanceof File)) throw new Error("Choose a document file to upload.");
    form.append("file", file);
    Object.entries(payload).forEach(([key, value]) => {
      if (key !== "file" && value !== undefined && value !== null && String(value).trim() !== "") {
        form.append(key, String(value));
      }
    });
    return unwrap<AnyRecord>(apiClient.post("/api/documents/upload", form, {
      headers: { "Content-Type": "multipart/form-data" },
      timeout: 120_000,
    }));
  },
  update: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.put(`/api/documents/${id}`, payload)),
  remove: (id: string | number) => unwrap<AnyRecord>(apiClient.delete(`/api/documents/${id}`)),
  expiring: () => unwrap<AnyRecord[]>(apiClient.get("/api/documents/expiring")),
  renew: (id: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/documents/${id}/renew`, {})),
  timeline: (id: string | number) => unwrap<AnyRecord[]>(apiClient.get(`/api/documents/${id}/timeline`)),
  recommendations: () => unwrap<AnyRecord[]>(apiClient.get("/api/documents/recommendations")),
};
