const BASE = import.meta.env.VITE_API_URL ?? "http://localhost:8088";

async function get<T>(path: string): Promise<T> {
  const r = await fetch(`${BASE}${path}`);
  if (!r.ok) throw new Error(`${r.status} ${path}`);
  const json = await r.json();
  return json.data ?? json;
}

export const aboutApi = {
  platform:      () => get("/api/about/platform"),
  healthSummary: () => get("/api/about/health-summary"),
};
