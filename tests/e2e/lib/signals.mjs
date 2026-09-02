export function isAllowedRequestFailure(request) {
  const navigationAbort = request?.method === "GET"
    && request?.resourceType === "document"
    && String(request?.failure || "").includes("ERR_ABORTED");
  const localAnonymousPreferenceBootstrap = request?.allowReason === "local-anonymous-preference-bootstrap"
    && request?.method === "GET"
    && request?.resourceType === "xhr"
    && new URL(request.url).pathname === "/api/localization/user-preferences";
  return navigationAbort || localAnonymousPreferenceBootstrap;
}

export function assertStagingAuthConfigured(target, role, configuredState) {
  if (target.environment === "staging" && role !== "anonymous" && !configuredState) {
    throw new Error(`E2E_${role.toUpperCase()}_AUTH_STATE must point to an existing storage-state file for staging certification`);
  }
}

export function apiRequestMatchesTarget(requestUrl, apiBaseUrl) {
  const observed = new URL(requestUrl);
  const expected = new URL(apiBaseUrl);
  const expectedPath = expected.pathname === "/" ? "/api/" : `${expected.pathname.replace(/\/$/, "")}/api/`;
  return observed.origin === expected.origin && observed.pathname.startsWith(expectedPath);
}

export function assertRuntimeSignalsHealthy(signals) {
  const consoleErrors = signals.consoleErrors ?? [];
  const pageErrors = signals.pageErrors ?? [];
  const serverErrors = signals.serverErrors ?? [];
  const apiTargetMismatches = signals.apiTargetMismatches ?? [];
  const failedRequests = signals.failedRequests ?? [];
  if (consoleErrors.length > 0) {
    throw new Error(`Browser console emitted errors: ${JSON.stringify(consoleErrors)}`);
  }
  if (pageErrors.length > 0) {
    throw new Error(`Browser page emitted runtime errors: ${JSON.stringify(pageErrors)}`);
  }
  if (serverErrors.length > 0) {
    throw new Error(`Browser journey received HTTP 5xx responses: ${JSON.stringify(serverErrors)}`);
  }
  if (apiTargetMismatches.length > 0) {
    throw new Error(`Rendered frontend API target does not match E2E_API_BASE_URL: ${JSON.stringify(apiTargetMismatches)}`);
  }
  const unexpectedFailures = failedRequests.filter((request) => !isAllowedRequestFailure(request));
  if (unexpectedFailures.length > 0) {
    throw new Error(`Browser journey emitted unexpected request failures: ${JSON.stringify(unexpectedFailures)}`);
  }
}
