/**
 * OpsTrax Enterprise Design System v4.0 — token source of truth (TS).
 *
 * `tokens` mirrors the CSS custom properties declared in `styles/index.css`
 * (`:root`) EXACTLY. Use it anywhere a CSS variable cannot be referenced —
 * principally Recharts and inline SVG `stroke`/`fill`/`color` props.
 *
 * `chart` is the extended data-visualization palette. Every value below already
 * existed verbatim as a hardcoded literal in chart/SVG code; this file simply
 * centralizes them. Names follow the existing Tailwind hue/scale identity of
 * each value. No new color values are introduced here — keep in lockstep with
 * `index.css`. If a CSS variable changes, change it here too.
 */

/* ---------- Core tokens — 1:1 with index.css :root ---------- */
export const tokens = {
  // Page & surface
  bg: "#eef3f9",
  bgDeep: "#e6edf6",
  surface: "#ffffff",
  surfaceRaised: "#fbfdff",
  surfaceSunken: "#eff4f9",

  // Borders
  border: "#e2e8f0",
  borderStrong: "#cbd5e1",
  borderAccent: "rgba(13,148,136,.3)",

  // Brand
  teal: "#0d9488",
  tealDim: "rgba(13,148,136,.1)",
  tealLight: "#ccfbf1",
  blue: "#2563eb",
  violet: "#7c3aed",

  // Text
  textPrimary: "#0f172a",
  textSecondary: "#475569",
  textMuted: "#94a3b8",

  // Radii
  rCard: "18px",
  rBtn: "12px",
  rField: "12px",
  rClay: "20px",

  // v5.0 blur scale (mirror of --blur-*)
  blurXs: "4px",
  blurSm: "8px",
  blurMd: "14px",
  blurLg: "22px",
} as const;

/* ---------- v5.0 status tokens — 1:1 with index.css --status-* ---------- */
export const status = {
  danger: "#dc2626",
  dangerBg: "#fef2f2",
  dangerBorder: "#fecaca",
  warning: "#d97706",
  warningBg: "#fffbeb",
  warningBorder: "#fde68a",
  success: "#059669",
  successBg: "#ecfdf5",
  successBorder: "#a7f3d0",
  info: "#2563eb",
  infoBg: "#eff6ff",
  infoBorder: "#bfdbfe",
  ai: "#7c3aed",
  aiBg: "#f5f3ff",
  aiBorder: "#ddd6fe",
  muted: "#64748b",
  mutedBg: "#f8fafc",
  mutedBorder: "#e2e8f0",
} as const;

/* ---------- Extended chart / data-viz palette ----------
   Each value is an existing literal lifted out of chart/SVG code. Several are
   intentional aliases of core tokens (noted) so chart code can read in one
   consistent vocabulary. */
export const chart = {
  // teal
  teal700: "#0f766e",
  teal600: "#0d9488", // alias tokens.teal
  teal500: "#14b8a6",
  teal400: "#2dd4bf",
  // blue / sky
  sky600: "#0284c7",
  sky400: "#38bdf8",
  blue600: "#2563eb", // alias tokens.blue
  blue500: "#3b82f6",
  blue400: "#60a5fa",
  // indigo / violet
  indigo600: "#4f46e5",
  indigo500: "#6366f1",
  violet600: "#7c3aed", // alias tokens.violet
  violet500: "#8b5cf6",
  // green
  emerald600: "#059669",
  emerald500: "#10b981",
  emerald400: "#34d399",
  // amber / orange
  amber600: "#d97706",
  amber500: "#f59e0b",
  orange500: "#f97316",
  // red / rose
  red600: "#dc2626",
  red500: "#ef4444",
  red400: "#f87171",
  rose600: "#e11d48",
  // neutral (axes, labels, gridlines, tooltips)
  slate700: "#334155",
  slate600: "#475569", // alias tokens.textSecondary
  slate500: "#64748b",
  slate400: "#94a3b8", // alias tokens.textMuted
  surface: "#ffffff", // alias tokens.surface
  border: "#e2e8f0", // alias tokens.border
} as const;
