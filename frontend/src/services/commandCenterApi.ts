import { apiClient, unwrap } from "./apiClient";
import type { AnyRecord } from "@/types";

export const commandCenterApi = {
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/command-center/summary")),
};
