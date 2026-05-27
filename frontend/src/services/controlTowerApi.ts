import { apiClient, unwrap } from "./apiClient";
import type { AnyRecord } from "@/types";

export const controlTowerApi = {
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/control-tower/summary")),
  entities: () => unwrap<AnyRecord[]>(apiClient.get("/api/control-tower/entities")),
  events: () => unwrap<AnyRecord[]>(apiClient.get("/api/control-tower/events")),
  sendEta: () => unwrap<AnyRecord>(apiClient.post("/api/control-tower/actions/send-eta-update", {})),
};
