import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";

// Agentic Ops Copilot — proposed AI actions the dispatcher approves or dismisses.
// The agent writes status='proposed' recommendations server-side; these calls surface
// them and route approval through the human-gated executor.
export const aiCopilotApi = {
  proposed: () => unwrap<AnyRecord[]>(apiClient.get("/api/ai/recommendations?status=proposed")),
  approve: (id: number | string) => unwrap<AnyRecord>(apiClient.post(`/api/ai/recommendations/${id}/approve`, {})),
  dismiss: (id: number | string) => unwrap<AnyRecord>(apiClient.post(`/api/ai/recommendations/${id}/dismiss`, {})),
};
