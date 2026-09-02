const DYNAMIC_IMPORT_FAILURE_PATTERNS = [
  /failed to fetch dynamically imported module/i,
  /error loading dynamically imported module/i,
  /importing a module script failed/i,
  /chunkloaderror/i,
  /loading chunk [^\s]+ failed/i,
];

function errorMessage(error: unknown): string {
  if (error instanceof Error) {
    return `${error.name}: ${error.message}`;
  }

  return String(error);
}

export function isDynamicImportFailure(error: unknown): boolean {
  const message = errorMessage(error);
  return DYNAMIC_IMPORT_FAILURE_PATTERNS.some((pattern) => pattern.test(message));
}

export function shouldReloadForDynamicImportFailure(
  error: unknown,
  alreadyAttempted: boolean,
): boolean {
  return isDynamicImportFailure(error) && !alreadyAttempted;
}

export function moduleLoadRecoveryKey(frontendSha: string): string {
  return `opstrax:module-load-recovery:${frontendSha}`;
}
