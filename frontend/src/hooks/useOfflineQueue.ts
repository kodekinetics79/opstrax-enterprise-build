import { useCallback, useEffect, useRef, useState } from "react";

// Offline-aware action queue for the driver mobile workflow.
//
// SAFE to queue offline (idempotent drafts that don't mutate dispatch state machine):
//   dvir_draft, exception_draft, proof_draft, notes_draft
//
// UNSAFE to queue — requires live backend validation (state machine mutations):
//   accept_assignment, status_transition, delivered (final), cancel
//
// Each queued item has an idempotency_key (generated once at creation time).
// Duplicate keys are silently discarded on submission.

const QUEUE_KEY = "driver_offline_queue";

export type QueuedActionType =
  | "dvir_draft"
  | "exception_draft"
  | "proof_draft"
  | "notes_draft";

export interface QueuedAction {
  idempotencyKey: string;
  actionType: QueuedActionType;
  payload: Record<string, unknown>;
  createdAt: string;
  status: "pending" | "processing" | "failed";
  retryCount: number;
  errorMessage?: string;
}

function generateIdempotencyKey(): string {
  return `${Date.now()}-${Math.random().toString(36).slice(2, 11)}`;
}

function loadQueue(): QueuedAction[] {
  try {
    const raw = localStorage.getItem(QUEUE_KEY);
    return raw ? (JSON.parse(raw) as QueuedAction[]) : [];
  } catch {
    return [];
  }
}

function saveQueue(queue: QueuedAction[]): void {
  try {
    localStorage.setItem(QUEUE_KEY, JSON.stringify(queue));
  } catch {
    // Storage full — discard oldest pending items
  }
}

export function useOfflineQueue() {
  const [isOnline, setIsOnline] = useState(navigator.onLine);
  const [queue, setQueue] = useState<QueuedAction[]>(loadQueue);
  const processingRef = useRef(false);

  useEffect(() => {
    const onOnline  = () => setIsOnline(true);
    const onOffline = () => setIsOnline(false);
    window.addEventListener("online",  onOnline);
    window.addEventListener("offline", onOffline);
    return () => {
      window.removeEventListener("online",  onOnline);
      window.removeEventListener("offline", onOffline);
    };
  }, []);

  // Persist queue changes to localStorage
  useEffect(() => { saveQueue(queue); }, [queue]);

  const enqueue = useCallback((
    actionType: QueuedActionType,
    payload: Record<string, unknown>
  ): string => {
    const idempotencyKey = generateIdempotencyKey();
    const action: QueuedAction = {
      idempotencyKey,
      actionType,
      payload,
      createdAt: new Date().toISOString(),
      status: "pending",
      retryCount: 0,
    };
    setQueue(prev => {
      // Discard if same key already exists
      if (prev.some(a => a.idempotencyKey === idempotencyKey)) return prev;
      return [...prev, action];
    });
    return idempotencyKey;
  }, []);

  const remove = useCallback((idempotencyKey: string) => {
    setQueue(prev => prev.filter(a => a.idempotencyKey !== idempotencyKey));
  }, []);

  const clearProcessed = useCallback(() => {
    setQueue(prev => prev.filter(a => a.status !== "pending" ? false : true));
  }, []);

  // Process pending queue when back online.
  // Only dvir_draft, proof_draft, exception_draft, notes_draft are safe to auto-process.
  // State machine mutations (accept, major status changes, delivered) are NOT in the queue.
  const processQueue = useCallback(async (
    processor: (action: QueuedAction) => Promise<void>
  ) => {
    if (!isOnline || processingRef.current) return;
    const pending = queue.filter(a => a.status === "pending");
    if (pending.length === 0) return;

    processingRef.current = true;
    for (const action of pending) {
      setQueue(prev =>
        prev.map(a => a.idempotencyKey === action.idempotencyKey ? { ...a, status: "processing" } : a)
      );
      try {
        await processor(action);
        setQueue(prev => prev.filter(a => a.idempotencyKey !== action.idempotencyKey));
      } catch (err) {
        const msg = err instanceof Error ? err.message : "Unknown error";
        setQueue(prev =>
          prev.map(a =>
            a.idempotencyKey === action.idempotencyKey
              ? { ...a, status: "failed", retryCount: a.retryCount + 1, errorMessage: msg }
              : a
          )
        );
      }
    }
    processingRef.current = false;
  }, [isOnline, queue]);

  return {
    isOnline,
    queue,
    pendingCount: queue.filter(a => a.status === "pending").length,
    enqueue,
    remove,
    clearProcessed,
    processQueue,
  };
}
