import { apiClient, unwrap } from "@/services/apiClient";
import type { AnyRecord } from "@/types";

export const safetyApi = {
  summary: () => unwrap<AnyRecord>(apiClient.get("/api/safety/summary")),
  events: () => unwrap<AnyRecord[]>(apiClient.get("/api/safety/events")),
  detail: (id: string | number) => unwrap<AnyRecord>(apiClient.get(`/api/safety/events/${id}`)),
  create: (payload: AnyRecord) => unwrap<AnyRecord>(apiClient.post("/api/safety/events", payload)),
  update: (id: string | number, payload: AnyRecord) => unwrap<AnyRecord>(apiClient.put(`/api/safety/events/${id}`, payload)),
  remove: (id: string | number) => unwrap<AnyRecord>(apiClient.delete(`/api/safety/events/${id}`)),
  driverScorecards: () => unwrap<AnyRecord[]>(apiClient.get("/api/safety/drivers/scorecards")),
  vehicleScorecards: () => unwrap<AnyRecord[]>(apiClient.get("/api/safety/vehicles/scorecards")),
  trends: () => unwrap<AnyRecord[]>(apiClient.get("/api/safety/trends")),
  recommendations: () => unwrap<AnyRecord[]>(apiClient.get("/api/safety/recommendations")),
  review: (id: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/safety/events/${id}/review`, {})),
  createCoaching: (id: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/safety/events/${id}/create-coaching-task`, {})),
  createIncident: (id: string | number) => unwrap<AnyRecord>(apiClient.post(`/api/safety/events/${id}/create-incident`, {})),
};
