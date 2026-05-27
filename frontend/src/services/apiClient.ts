import axios from "axios";
import type { ApiEnvelope } from "@/types";

export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || import.meta.env.VITE_DOTNET_API_URL || "http://localhost:8088";
export const NODE_EVENTS_URL = import.meta.env.VITE_NODE_EVENTS_URL || "http://localhost:8090";

export const apiClient = axios.create({
  baseURL: API_BASE_URL,
  headers: { Accept: "application/json" },
  timeout: 30000,
});

apiClient.interceptors.request.use((config) => {
  const session = localStorage.getItem("opstrax.session");
  if (session) {
    try {
      const token = JSON.parse(session).token;
      if (token) config.headers.Authorization = `Bearer ${token}`;
    } catch {
      localStorage.removeItem("opstrax.session");
    }
  }
  return config;
});

export async function unwrap<T>(request: Promise<{ data: ApiEnvelope<T> }>): Promise<T> {
  const response = await request;
  if (!response.data.success) throw new Error(response.data.message || "API request failed");
  return response.data.data;
}
