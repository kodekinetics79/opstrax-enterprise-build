import axios from "axios";
import type { ApiEnvelope } from "@/types";
import { getGlobalCsrfToken, setGlobalCsrfToken } from "@/hooks/useCsrf";

export const API_BASE_URL =
  import.meta.env.VITE_API_BASE_URL ||
  import.meta.env.VITE_DOTNET_API_URL ||
  "http://localhost:8088";

export const NODE_EVENTS_URL =
  import.meta.env.VITE_NODE_EVENTS_URL ||
  "http://localhost:8090";

export const apiClient = axios.create({
  baseURL: API_BASE_URL,
  headers: { Accept: "application/json" },
  timeout: 30000,
  withCredentials: true,
});

// Request interceptor: Add auth token and CSRF token
apiClient.interceptors.request.use((config) => {
  const session = localStorage.getItem("opstrax.session.v2") || localStorage.getItem("opstrax.session");
  if (session) {
    try {
      const parsed = JSON.parse(session);
      if (parsed.token) {
        config.headers.Authorization = `Bearer ${parsed.token}`;
      }
      if (parsed.csrfToken) {
        setGlobalCsrfToken(parsed.csrfToken);
      }
    } catch {
      localStorage.removeItem("opstrax.session");
    }
  }

  // Add CSRF token for state-changing requests
  const csrfToken = getGlobalCsrfToken();
  if (csrfToken && ["POST", "PUT", "DELETE", "PATCH"].includes(config.method?.toUpperCase() || "")) {
    config.headers["X-CSRF-Token"] = csrfToken;
  }

  return config;
});

// Response interceptor: Capture and store CSRF token
apiClient.interceptors.response.use((response) => {
  const csrfToken = response.headers["x-csrf-token"];
  if (csrfToken) {
    setGlobalCsrfToken(csrfToken);
  }
  return response;
});

export async function unwrap<T>(request: Promise<{ data: ApiEnvelope<T> }>): Promise<T> {
  const response = await request;
  if (!response.data.success) throw new Error(response.data.message || "API request failed");
  return response.data.data;
}
