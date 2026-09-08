// Dev-only offline data layer. Installed by main.tsx ONLY when both `import.meta.env.DEV`
// and `VITE_MOCK_DATA=true` are set — Vite statically eliminates this whole branch (and this
// module, since the import is dynamic) from any production build, so none of this can ship.
//
// Serves real data captured from a live, authenticated session (see fixtures.ts) instead of
// hitting a network at all, so the app renders its actual screens without a running backend.
import type { AxiosResponse, InternalAxiosRequestConfig } from "axios";
import { apiClient } from "@/services/apiClient";
import { FIXTURES } from "./fixtures";

// Real endpoints wrap a list under `data` in several different conventions (`data` itself is
// the array, or `data.items`/`data.rows`/`data.results` is). A plain `data: []` stub -- from
// an unmocked path, or from downgrading a live 500 to something harmless -- only satisfies
// the first convention; code expecting `data.items` then calls `.map()` on `undefined` and
// crashes. Since this is an in-memory JS object handed straight to axios (never
// JSON-serialized), an array can also carry named aliases pointing at itself, satisfying
// every convention a caller might use, whether the array is empty or has real captured rows.
function withListAliases(arr: unknown[]): unknown[] {
  const aliased = arr as unknown[] & Record<string, unknown>;
  aliased.items = aliased;
  aliased.rows = aliased;
  aliased.results = aliased;
  return aliased;
}

function decorateBody<T>(body: T): T {
  if (body && typeof body === "object" && "data" in body) {
    const envelope = body as { data: unknown };
    if (Array.isArray(envelope.data)) {
      return { ...body, data: withListAliases([...envelope.data]) };
    }
  }
  return body;
}

const EMPTY_LIST = { success: true, data: [], message: "", errors: [] };

function buildResponse<T>(config: InternalAxiosRequestConfig, status: number, body: T): AxiosResponse<T> {
  return {
    data: decorateBody(body),
    status,
    statusText: status < 300 ? "OK" : "Error",
    headers: {},
    config,
    request: {},
  };
}

async function mockAdapter(config: InternalAxiosRequestConfig): Promise<AxiosResponse> {
  const method = (config.method ?? "get").toLowerCase();
  const url = new URL(config.url ?? "", "http://mock.local");
  const pathname = url.pathname;

  // Simulate real latency so loading states are visible, not instant.
  await new Promise((resolve) => setTimeout(resolve, 120));

  if (method !== "get") {
    // Mutations (acknowledge defect, create coaching task, refresh alerts, ...) succeed but
    // don't persist -- this is a read-mostly visual preview, not a working backend.
    return buildResponse(config, 200, { success: true, data: {}, message: "Mocked (not persisted)", errors: [] });
  }

  const fixture = FIXTURES[pathname];
  if (fixture) {
    return buildResponse(config, fixture.status, fixture.body);
  }

  // Unknown GET (nothing captured for this path): default to an empty *array* rather than
  // guessing list-vs-object from the URL. decorateBody() aliases it with .items/.rows/.results,
  // so this satisfies both "data is the array" and "data.items is the array" callers -- an
  // object default only ever satisfies the second and crashes the first (`.map is not a
  // function`), which is exactly what broke a few list endpoints the URL heuristic missed
  // (e.g. /api/campaigns).
  return buildResponse(config, 200, EMPTY_LIST);
}

export function installMockAdapter(): void {
  apiClient.defaults.adapter = mockAdapter;
  // eslint-disable-next-line no-console
  console.info("[dev-mock] Serving captured live-site fixtures instead of a real backend.");
}
