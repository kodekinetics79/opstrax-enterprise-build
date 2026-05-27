import { apiClient, unwrap } from "./apiClient";
import type { AnyRecord } from "@/types";

export const controlTowerApi = {
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/control-tower/summary")),
  entities: () => unwrap<AnyRecord[]>(apiClient.get("/api/control-tower/entities")),
  entity: (entityType: string, id: string | number) => unwrap<AnyRecord>(apiClient.get(`/api/control-tower/entities/${entityType}/${id}`)),
  events: () => unwrap<AnyRecord[]>(apiClient.get("/api/control-tower/events")),
  sendEta: () => unwrap<AnyRecord>(apiClient.post("/api/control-tower/actions/send-eta-update", {})),
  createDispatchReview: () => unwrap<AnyRecord>(apiClient.post("/api/control-tower/actions/create-dispatch-review", {})),
  createMaintenanceReview: () => unwrap<AnyRecord>(apiClient.post("/api/control-tower/actions/create-maintenance-review", {})),
};
