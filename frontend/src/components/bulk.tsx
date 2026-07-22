import { useEffect, useRef, useState, type ReactNode } from "react";

// ─── Shared bulk-selection primitives ─────────────────────────────────────────
// Lifted verbatim out of pages/platform/ui.tsx (which held the ONLY multi-select
// implementation in the codebase) so the tenant app and the platform control
// plane share one selection model. Behaviour is identical: shift-click range
// selection with an anchor, an indeterminate header checkbox, and a sticky
// action bar. platform/ui.tsx now re-exports these under their original
// P-prefixed names, so the platform pages compile unchanged.
//
// Selection is keyed by string id. Callers pass the CURRENTLY VISIBLE (filtered)
// ids, so "select all" only ever targets rows the operator can see, while any
// selected id that scrolls out of the active filter is preserved.
export function useRowSelection(visibleIds: unknown[]) {
  const [selected, setSelected] = useState<Set<string>>(new Set());
  // Anchor for shift-click / Shift+Space range selection (the last row toggled).
  const anchorRef = useRef<string | null>(null);
  const ids = visibleIds.map(String);
  const allVisibleSelected = ids.length > 0 && ids.every((id) => selected.has(id));
  const someVisibleSelected = ids.some((id) => selected.has(id));

  // toggle(id, shiftKey): a plain toggle flips one row and sets the anchor. A
  // shift-toggle selects the whole contiguous range between the anchor and this
  // row (Gmail/Finder semantics), so you can pick many rows in two clicks/keys.
  const toggle = (id: unknown, shiftKey = false) => {
    const key = String(id);
    const idx = ids.indexOf(key);
    setSelected((prev) => {
      const next = new Set(prev);
      const anchor = anchorRef.current;
      if (shiftKey && anchor !== null && idx !== -1) {
        const aIdx = ids.indexOf(anchor);
        if (aIdx !== -1) {
          const [lo, hi] = aIdx <= idx ? [aIdx, idx] : [idx, aIdx];
          for (let i = lo; i <= hi; i++) next.add(ids[i]);
          return next;
        }
      }
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
    anchorRef.current = key;
  };

  const toggleAllVisible = () =>
    setSelected((prev) => {
      const next = new Set(prev);
      if (ids.every((id) => prev.has(id))) ids.forEach((id) => next.delete(id));
      else ids.forEach((id) => next.add(id));
      return next;
    });

  return {
    selectedIds: [...selected],
    count: selected.size,
    isSelected: (id: unknown) => selected.has(String(id)),
    toggle,
    toggleAllVisible,
    allVisibleSelected,
    someVisibleSelected,
    clear: () => { anchorRef.current = null; setSelected(new Set()); },
  };
}

// Native checkbox: fully keyboard-operable out of the box (Tab to focus, Space to
// toggle, Shift+Space to range-select). We handle the toggle in onClick so we can
// read `shiftKey` — which is set for both mouse clicks and keyboard Space — and
// keep onChange as a no-op to satisfy React's controlled-input contract.
export function BulkCheckbox({ checked, indeterminate, onToggle, ariaLabel }: {
  checked: boolean; indeterminate?: boolean; onToggle: (shiftKey: boolean) => void; ariaLabel: string;
}) {
  return (
    <input
      type="checkbox"
      checked={checked}
      ref={(el) => { if (el) el.indeterminate = Boolean(indeterminate) && !checked; }}
      onChange={() => {}}
      onClick={(e) => { e.stopPropagation(); onToggle(e.shiftKey); }}
      aria-label={ariaLabel}
      className="h-4 w-4 cursor-pointer rounded border-slate-300 text-teal-500 focus:ring-2 focus:ring-teal-400/30"
    />
  );
}

// Sticky action bar shown while ≥1 row is selected. `children` are the action
// buttons, already permission-filtered by the caller.
export function BulkBar({ count, onClear, children }: {
  count: number; onClear: () => void; children: ReactNode;
}) {
  if (count === 0) return null;
  return (
    <div className="sticky bottom-4 z-40 flex flex-wrap items-center gap-3 rounded-[16px] border border-slate-300 bg-white/95 px-4 py-3 shadow-lg backdrop-blur">
      <span className="text-sm font-semibold text-slate-700">{count} selected</span>
      <div className="flex flex-wrap items-center gap-2">{children}</div>
      <button type="button" onClick={onClear} className="ml-auto text-sm font-medium text-slate-500 hover:text-slate-700">Clear</button>
    </div>
  );
}

// Local copies of the platform button/input chrome. Inlined rather than imported
// from pages/platform/ui.tsx so this module has no dependency on the page layer
// (ui.tsx imports FROM here — importing back would be circular). Class lists are
// identical to PButton/PInput, so the platform dialogs render byte-for-byte the same.
const DIALOG_BTN = "inline-flex items-center justify-center gap-2 rounded-[14px] px-4 py-2 text-sm font-semibold transition disabled:opacity-50";
const DIALOG_BTN_VARIANT = {
  primary: "bg-gradient-to-r from-teal-400 to-cyan-400 text-slate-950 hover:brightness-105 shadow-sm",
  ghost: "border border-slate-300 bg-white text-slate-700 hover:border-slate-400 hover:bg-slate-50",
  danger: "border border-red-500/30 bg-red-50 text-red-700 hover:bg-red-100",
} as const;
const DIALOG_INPUT = "w-full rounded-[14px] border border-slate-300 bg-white px-3 py-2.5 text-sm text-slate-900 placeholder:text-slate-400 outline-none shadow-sm focus:border-teal-400 focus:ring-2 focus:ring-teal-400/15";

// Destructive-action confirmation. When `confirmText` is provided the user must
// type it exactly (e.g. the tenant code, or DELETE for a bulk soft-delete) before
// the confirm button enables — required by the control-plane standard for
// cancel/offboard/delete-class actions.
export function ConfirmDialog({ open, title, body, confirmLabel = "Confirm", confirmText, danger = true, busy, onConfirm, onClose }: {
  open: boolean; title: string; body?: ReactNode; confirmLabel?: string; confirmText?: string;
  danger?: boolean; busy?: boolean; onConfirm: () => void; onClose: () => void;
}) {
  const [typed, setTyped] = useState("");
  const ref = useRef<HTMLDivElement>(null);

  // Focus trap + Escape-to-close + focus restoration — the destructive confirm
  // must behave like the sibling Modal: Escape dismisses THIS dialog (and is
  // stopped from bubbling to any page-level Escape handler behind it), Tab wraps
  // inside the dialog, and focus returns to the trigger on close. Capture phase
  // so we intercept before the page listener; guarded on `open` so the listener
  // is only bound while the dialog is mounted.
  useEffect(() => {
    if (!open) return;
    const prev = document.activeElement as HTMLElement | null;
    const node = ref.current;
    const focusables = () =>
      node
        ? Array.from(
            node.querySelectorAll<HTMLElement>(
              'button,[href],input,select,textarea,[tabindex]:not([tabindex="-1"])',
            ),
          ).filter((el) => !el.hasAttribute("disabled"))
        : [];
    // Only steal focus if it isn't already inside the dialog (the confirm-text
    // input auto-focuses itself when present).
    if (!node?.contains(document.activeElement)) focusables()[0]?.focus();

    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.stopPropagation();
        onClose();
        return;
      }
      if (e.key === "Tab") {
        const items = focusables();
        if (items.length === 0) return;
        const first = items[0];
        const last = items[items.length - 1];
        if (e.shiftKey && document.activeElement === first) {
          e.preventDefault();
          last.focus();
        } else if (!e.shiftKey && document.activeElement === last) {
          e.preventDefault();
          first.focus();
        }
      }
    };
    document.addEventListener("keydown", onKey, true);
    return () => {
      document.removeEventListener("keydown", onKey, true);
      prev?.focus?.();
    };
  }, [open, onClose]);

  if (!open) return null;
  const blocked = Boolean(confirmText) && typed !== confirmText;
  return (
    <div className="fixed inset-0 z-60 flex items-center justify-center bg-slate-950/60 backdrop-blur-sm" onClick={onClose}>
      <div
        ref={ref}
        role="alertdialog"
        aria-modal="true"
        className="w-full max-w-md rounded-[20px] border border-slate-200 bg-white p-6 shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        <h2 className="text-lg font-bold text-slate-950">{title}</h2>
        {body && <div className="mt-2 text-sm leading-6 text-slate-600">{body}</div>}
        {confirmText && (
          <div className="mt-4">
            <p className="mb-1.5 text-xs font-semibold uppercase tracking-wider text-slate-500">
              Type <span className="font-mono text-slate-800">{confirmText}</span> to confirm
            </p>
            <input
              className={DIALOG_INPUT}
              value={typed}
              onChange={(e) => setTyped(e.target.value)}
              placeholder={confirmText}
              autoFocus
            />
          </div>
        )}
        <div className="mt-5 flex justify-end gap-2">
          <button type="button" className={`${DIALOG_BTN} ${DIALOG_BTN_VARIANT.ghost}`} onClick={onClose} disabled={busy}>Cancel</button>
          <button
            type="button"
            className={`${DIALOG_BTN} ${danger ? DIALOG_BTN_VARIANT.danger : DIALOG_BTN_VARIANT.primary}`}
            onClick={onConfirm}
            disabled={busy || blocked}
          >
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
}
