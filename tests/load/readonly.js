import { check } from "k6";
import http from "k6/http";

const rate = Number(__ENV.K6_ITERATIONS_PER_SECOND);
const duration = Number(__ENV.K6_DURATION_SECONDS);
const maxVus = Number(__ENV.K6_MAX_VUS);

export const options = {
  discardResponseBodies: true,
  scenarios: {
    readonly_api: {
      executor: "constant-arrival-rate",
      rate,
      timeUnit: "1s",
      duration: `${duration}s`,
      preAllocatedVUs: Math.min(maxVus, Math.max(2, rate)),
      maxVUs: maxVus,
      gracefulStop: "10s",
    },
  },
  thresholds: {
    checks: ["rate>0.99"],
    dropped_iterations: ["count==0"],
    http_req_failed: ["rate<0.01"],
    // The versioned product SLO in docs/RELIABILITY_RUNBOOK.md is p95 < 500 ms.
    // Keep the aggregate and each customer-facing surface independently below it
    // so a fast health check cannot conceal a slow authenticated fleet read.
    http_req_duration: ["p(95)<500", "p(99)<5000"],
    "http_req_duration{surface:public-health}": ["p(95)<500", "p(99)<5000"],
    "http_req_duration{surface:authenticated-read}": ["p(95)<500", "p(99)<5000"],
  },
};

export default function readonlyApiJourney() {
  const publicResponse = http.get(`${__ENV.K6_API_BASE_URL}${__ENV.K6_PUBLIC_PATH}`, {
    redirects: 0,
    tags: { surface: "public-health", method: "GET" },
  });
  check(publicResponse, { "public GET is 200": (response) => response.status === 200 });

  const authenticatedResponse = http.get(`${__ENV.K6_API_BASE_URL}${__ENV.K6_AUTHENTICATED_PATH}`, {
    headers: { Authorization: `Bearer ${__ENV.K6_BEARER_TOKEN}` },
    redirects: 0,
    tags: { surface: "authenticated-read", method: "GET" },
  });
  check(authenticatedResponse, { "authenticated GET is 200": (response) => response.status === 200 });
}
