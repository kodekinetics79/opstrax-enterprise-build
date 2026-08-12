export function assertRuntimeSignalsHealthy(signals) {
  if (signals.pageErrors.length > 0) {
    throw new Error(`Browser page emitted runtime errors: ${JSON.stringify(signals.pageErrors)}`);
  }
  if (signals.serverErrors.length > 0) {
    throw new Error(`Browser journey received HTTP 5xx responses: ${JSON.stringify(signals.serverErrors)}`);
  }
}
