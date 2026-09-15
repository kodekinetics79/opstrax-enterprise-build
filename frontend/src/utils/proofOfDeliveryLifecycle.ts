type ProofOfDeliveryRow = {
  jobStatus?: unknown;
  status?: unknown;
};

const CAPTURE_READY_JOB_STATUSES = new Set(["at stop", "completed", "delivered"]);
const CAPTURE_ACTION_PROOF_STATUSES = new Set(["pending", "awaiting capture", "rejected"]);

function normalizeStatus(value: unknown): string {
  return String(value ?? "")
    .trim()
    .toLowerCase()
    .replace(/[\s_-]+/g, " ");
}

export function isPodCaptureActionVisible(row: ProofOfDeliveryRow): boolean {
  return CAPTURE_ACTION_PROOF_STATUSES.has(normalizeStatus(row.status));
}

export function isPodCaptureReady(row: ProofOfDeliveryRow): boolean {
  return isPodCaptureActionVisible(row) && CAPTURE_READY_JOB_STATUSES.has(normalizeStatus(row.jobStatus));
}

export function podCaptureBlockedReason(row: ProofOfDeliveryRow): string | undefined {
  if (!isPodCaptureActionVisible(row) || isPodCaptureReady(row)) return undefined;
  return "Proof can be captured after the job reaches At Stop, Completed, or Delivered.";
}
