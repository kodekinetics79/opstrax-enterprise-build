import { apiClient, unwrap } from "./apiClient";
import type { AnyRecord } from "@/types";

export const aiApi = {
  insights: () => unwrap<AnyRecord[]>(apiClient.get("/api/ai/insights")),
  ask: (prompt: string, category: string) => unwrap<AnyRecord>(apiClient.post("/api/ai/ask", { prompt, category })),
};
