import { useEffect, useRef } from "react";

const FOCUSABLE = [
  "button:not([disabled])",
  "[href]",
  "input:not([disabled])",
  "select:not([disabled])",
  "textarea:not([disabled])",
  '[tabindex]:not([tabindex="-1"])',
].join(",");

/** Place focus inside, contain Tab, close on Escape, and restore the trigger. */
export function useDialogFocus<T extends HTMLElement>(open: boolean, onClose: () => void) {
  const dialogRef = useRef<T>(null);
  const closeRef = useRef(onClose);
  closeRef.current = onClose;

  useEffect(() => {
    if (!open) return;
    const previous = document.activeElement as HTMLElement | null;
    // Most new dialogs bind the returned ref. Legacy custom dialogs may be
    // compressed inline; fall back to the topmost rendered ARIA dialog so they
    // can adopt the same contract without duplicating keyboard logic.
    const discovered = Array.from(document.querySelectorAll<HTMLElement>('[role="dialog"],[role="alertdialog"]')).at(-1) ?? null;
    const node = dialogRef.current ?? discovered;
    const focusables = () => node
      ? Array.from(node.querySelectorAll<HTMLElement>(FOCUSABLE)).filter((item) => !item.hidden)
      : [];
    const preferred = node?.querySelector<HTMLElement>("[autofocus]") ?? focusables()[0];
    if (preferred && !node?.contains(document.activeElement)) preferred.focus();

    const isTopmostDialog = () => {
      if (!node) return false;
      const dialogs = Array.from(document.querySelectorAll<HTMLElement>('[role="dialog"],[role="alertdialog"]'))
        .filter((dialog) => dialog.isConnected && !dialog.hidden);
      return dialogs.at(-1) === node;
    };

    const onKeyDown = (event: KeyboardEvent) => {
      // Nested drawers and action modals each register this hook. Only the
      // visually topmost dialog may consume keyboard input.
      if (!isTopmostDialog()) return;
      if (event.key === "Escape") {
        event.preventDefault();
        event.stopPropagation();
        closeRef.current();
        return;
      }
      if (event.key !== "Tab") return;
      const items = focusables();
      if (!items.length) { event.preventDefault(); return; }
      const first = items[0];
      const last = items[items.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };
    document.addEventListener("keydown", onKeyDown, true);
    return () => {
      document.removeEventListener("keydown", onKeyDown, true);
      if (previous?.isConnected) previous.focus();
    };
  }, [open]);

  return dialogRef;
}
