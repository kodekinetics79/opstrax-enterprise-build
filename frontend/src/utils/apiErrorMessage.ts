type ErrorResponse = {
  response?: {
    data?: unknown;
  };
};

const genericHttpMessage = /^request failed with status code \d+$/i;
const stackFrame = /(?:^|\n)\s*at\s+\S+/i;

function safeErrorText(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const text = value.trim();
  if (!text || text.length > 1200 || genericHttpMessage.test(text) || stackFrame.test(text)) return null;
  return text;
}

/** Extracts only customer-safe fields from a handled API rejection. */
export function apiErrorMessage(error: unknown, fallback: string): string {
  const payload = (error as ErrorResponse | null)?.response?.data;
  if (payload && typeof payload === "object") {
    const envelope = payload as { message?: unknown; error?: unknown; errors?: unknown };
    const serverMessage = safeErrorText(envelope.message)
      ?? safeErrorText(envelope.error)
      ?? (Array.isArray(envelope.errors) ? safeErrorText(envelope.errors[0]) : null);
    if (serverMessage) return serverMessage;
  }

  const localMessage = error instanceof Error ? safeErrorText(error.message) : null;
  return localMessage ?? fallback;
}
