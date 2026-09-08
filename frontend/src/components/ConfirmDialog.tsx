import { useEffect, useId, useRef } from "react";
import type { ReactNode } from "react";
import { X } from "lucide-react";

const FOCUSABLE_SELECTOR =
  'button:not([disabled]), input:not([disabled]), textarea:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])';

/** Focus an element that is not natively focusable (a landmark), without leaving it in the tab order. */
function focusLandmark(element: HTMLElement) {
  if (!element.hasAttribute("tabindex")) element.setAttribute("tabindex", "-1");
  element.focus();
}

/**
 * Accessible in-app confirmation dialog. Replaces native window.confirm, which
 * automation cannot complete and assistive technology cannot inspect.
 *
 * A11y model mirrors the page-level ModalForm pattern: role="dialog",
 * aria-modal, a labelled title, a described message, a focus trap,
 * Escape = cancel, and focus restore to the invoking control on close.
 *
 * Two things this gets right that the naive version does not:
 *
 * 1. FOCUS RESTORE. Capturing `document.activeElement` on mount lands on
 *    `<body>`: the row-action menu that opened this dialog unmounts in the SAME
 *    commit that mounts the dialog, so the "previously focused" element is
 *    already detached. The opener must be captured at the moment the action is
 *    chosen and handed in as `returnFocusTo`. Even then the opener may be gone
 *    after a successful archive (its row disappeared), so we fall back to a
 *    stable page landmark rather than dropping focus on `<body>`.
 *
 * 2. THE TRAP DURING `busy`. Disabling every control puts focus on `<body>`,
 *    and Tab from `<body>` walks the background page — the trap only wraps when
 *    focus is on the dialog's own first/last focusable. Cancel therefore stays
 *    FOCUSABLE while busy (aria-disabled, not disabled) so the dialog always
 *    owns focus, while its handler still refuses to act — the dialog cannot
 *    silently detach from an in-flight outcome. Tab also pulls focus back
 *    whenever it has escaped the dialog.
 */
export function ConfirmDialog({
  title,
  message,
  confirmLabel,
  cancelLabel = "Cancel",
  variant = "default",
  busy = false,
  error = null,
  returnFocusTo = null,
  fallbackFocusSelector = "main",
  onConfirm,
  onCancel,
}: {
  title: string;
  message: ReactNode;
  confirmLabel: string;
  cancelLabel?: string;
  variant?: "default" | "danger";
  busy?: boolean;
  error?: string | null;
  /** The control that opened the dialog, captured BEFORE it unmounted. */
  returnFocusTo?: HTMLElement | null;
  /** Stable landmark to focus when the opener is gone (e.g. its row was archived). */
  fallbackFocusSelector?: string;
  onConfirm: () => void;
  onCancel: () => void;
}) {
  const titleId = useId();
  const messageId = useId();
  const dialogRef = useRef<HTMLDivElement>(null);
  const cancelButton = useRef<HTMLButtonElement>(null);
  const onCancelRef = useRef(onCancel);
  onCancelRef.current = onCancel;
  const busyRef = useRef(busy);
  busyRef.current = busy;
  const returnFocusRef = useRef(returnFocusTo);
  returnFocusRef.current = returnFocusTo;
  const fallbackSelectorRef = useRef(fallbackFocusSelector);
  fallbackSelectorRef.current = fallbackFocusSelector;

  useEffect(() => {
    cancelButton.current?.focus();
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape" && !busyRef.current) onCancelRef.current();
      if (event.key === "Tab") {
        const focusable = Array.from(dialogRef.current?.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR) ?? []);
        if (!focusable.length) { event.preventDefault(); return; }
        const first = focusable[0];
        const last = focusable[focusable.length - 1];
        const active = document.activeElement;
        // Focus escaped the dialog (a disabled control dropped it on <body>, or a
        // click landed on the backdrop). Nothing wraps in that state, so pull it back.
        if (!(active instanceof HTMLElement) || !dialogRef.current?.contains(active)) {
          event.preventDefault();
          (event.shiftKey ? last : first).focus();
          return;
        }
        if (event.shiftKey && active === first) { event.preventDefault(); last.focus(); }
        else if (!event.shiftKey && active === last) { event.preventDefault(); first.focus(); }
      }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => {
      window.removeEventListener("keydown", onKeyDown);
      const opener = returnFocusRef.current;
      // isConnected is the point: after a successful archive the triggering row —
      // and its menu button — are gone from the document, and focusing a detached
      // node silently moves focus to <body>.
      if (opener && opener.isConnected) { opener.focus(); return; }
      const landmark = fallbackSelectorRef.current
        ? document.querySelector<HTMLElement>(fallbackSelectorRef.current)
        : null;
      if (landmark) focusLandmark(landmark);
    };
  }, []);

  // Toggling `busy` disables the confirm button under the user's focus, which drops
  // focus to <body>. Re-seat it on Cancel, which stays focusable throughout.
  useEffect(() => {
    const active = document.activeElement;
    if (!(active instanceof HTMLElement) || !dialogRef.current?.contains(active)) {
      cancelButton.current?.focus();
    }
  }, [busy]);

  return (
    <div className="fixed inset-0 z-[70] grid place-items-center bg-black/60 p-4" role="presentation">
      <div ref={dialogRef} role="dialog" aria-modal="true" aria-labelledby={titleId} aria-describedby={messageId} className="panel max-h-[calc(100dvh-2rem)] w-full max-w-md overflow-y-auto p-4">
        <div className="flex items-start justify-between gap-3">
          <h2 id={titleId} className="min-w-0 break-words text-xl font-semibold text-slate-900">{title}</h2>
          <button type="button" className="icon-btn" aria-label={`Close ${title}`} disabled={busy} onClick={onCancel}><X className="h-4 w-4" /></button>
        </div>
        <div id={messageId} className="mt-3 text-sm text-slate-600">{message}</div>
        {error ? <div role="alert" className="mt-4 rounded-xl border border-red-200 bg-red-50 p-3 text-sm text-red-700">{error}</div> : null}
        <div className="mt-4 flex flex-wrap justify-end gap-2">
          <button
            ref={cancelButton}
            type="button"
            className={busy ? "btn-ghost cursor-not-allowed opacity-50" : "btn-ghost"}
            aria-disabled={busy || undefined}
            onClick={() => { if (!busy) onCancel(); }}
          >
            {cancelLabel}
          </button>
          <button type="button" className={variant === "danger" ? "btn-danger btn-cta" : "btn-primary btn-cta"} disabled={busy} onClick={onConfirm}>
            {busy ? "Working..." : confirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
}
