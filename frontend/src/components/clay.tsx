/* ============================================================================
   CLAY — OpsTrax tactile design system
   ----------------------------------------------------------------------------
   Claymorphism (puffy shapes, DOUBLE drop shadow) + Skeuomorphism (grain,
   bevels, physical depth) + Neumorphism (paired INSET/OUTSET extrusion).

   Design rules this file holds itself to:
     1. WCAG AA. Neumorphism's classic failure is low-contrast mush. Every
        text/background pair below is >= 4.5:1 (verified by hand, see TONES).
        Depth is carried by SHADOW, never by lowering text contrast.
     2. Built on the existing token palette in styles/index.css :root
        (--teal / --blue / --violet / --surface / --border). No new brand hue.
     3. prefers-reduced-motion: all transitions/animations collapse.
     4. Viewport-filling: ClayCard/ClayWell expose `fill` so a page can build a
        100vh flex column with internally-scrolling regions and zero dead space.

   Why a style element instead of tailwind.config: the repo is Tailwind v4
   CSS-first with NO config file, and this module is not allowed to edit
   index.css. Pseudo-classes (:active shadow inversion), keyframes and media
   queries cannot be expressed as inline styles, so the module ships its own
   stylesheet, injected once, namespaced `cx-` so it can never collide with the
   existing `.clay-card` / `.login2-*` rules.
   ========================================================================== */

import {
  forwardRef,
  useId,
  useMemo,
  type ButtonHTMLAttributes,
  type CSSProperties,
  type HTMLAttributes,
  type InputHTMLAttributes,
  type ReactNode,
  type SelectHTMLAttributes,
} from "react";
import {
  ChevronDown,
  Loader2,
  Minus,
  TrendingDown,
  TrendingUp,
  type LucideIcon,
} from "lucide-react";

/* ---------------------------------------------------------------------------
   Stylesheet (injected once per document)
   ------------------------------------------------------------------------ */

const STYLE_ID = "opstrax-clay-ds";

/* Skeuomorphic grain: fractal noise, inlined so no network request and no CSP
   surprise. Sits at 3% opacity — enough to feel like a material, far too faint
   to move a contrast ratio. */
const GRAIN =
  "url(\"data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='140' height='140'%3E%3Cfilter id='n'%3E%3CfeTurbulence type='fractalNoise' baseFrequency='.85' numOctaves='3' stitchTiles='stitch'/%3E%3C/filter%3E%3Crect width='140' height='140' filter='url(%23n)' opacity='.5'/%3E%3C/svg%3E\")";

const CSS = `
:root {
  /* Clay material — warm-neutral molded surface over the cool brand */
  --cx-r-card: 20px;
  --cx-r-btn: 12px;
  --cx-r-field: 12px;

  --cx-bg:        linear-gradient(150deg, #ffffff 0%, #f5f8fc 54%, #eaf1f8 100%);
  --cx-bg-sunken: linear-gradient(180deg, #e9eff7 0%, #f1f5fa 100%);
  --cx-edge:      rgba(255,255,255,.85);

  /* Neumorphic light/shade pair (matches the --fc-hi/--fc-lo pair already in :root) */
  --cx-hi: rgba(255,255,255,.94);
  --cx-lo: rgba(141,157,184,.38);

  /* OUTSET — puffy clay. One TIGHT dark shadow + one WIDE diffuse shadow
     (the double shadow), plus an inset top highlight so it reads as raised. */
  --cx-out:
    inset 0 2px 0 rgba(255,255,255,.95),
    inset 0 -9px 18px rgba(148,163,184,.13),
    0 2px 4px rgba(15,23,42,.06),
    0 16px 32px -12px rgba(15,23,42,.16),
    0 32px 64px -30px rgba(13,148,136,.24);
  --cx-out-hover:
    inset 0 2px 0 rgba(255,255,255,.95),
    inset 0 -9px 18px rgba(148,163,184,.11),
    0 3px 6px rgba(15,23,42,.07),
    0 22px 42px -14px rgba(15,23,42,.20),
    0 46px 92px -34px rgba(13,148,136,.32);

  /* Extruded control (button / knob) — soft neumorphic lift */
  --cx-raise:
    inset 0 1px 0 rgba(255,255,255,.95),
    -5px -5px 12px var(--cx-hi),
    7px 9px 20px var(--cx-lo);

  /* INSET — pressed in. The neumorphic hallmark: fields, wells, stat values. */
  --cx-in:
    inset 3px 3px 7px rgba(141,157,184,.34),
    inset -3px -3px 8px rgba(255,255,255,.92);
  --cx-in-deep:
    inset 5px 5px 12px rgba(141,157,184,.40),
    inset -4px -4px 10px rgba(255,255,255,.90),
    inset 0 1px 2px rgba(51,65,85,.10);
  --cx-in-focus:
    inset 3px 3px 7px rgba(141,157,184,.28),
    inset -3px -3px 8px rgba(255,255,255,.95),
    0 0 0 3px rgba(13,148,136,.22);
  --cx-in-error:
    inset 3px 3px 7px rgba(141,157,184,.28),
    inset -3px -3px 8px rgba(255,255,255,.95),
    0 0 0 3px rgba(220,38,38,.20);
}

/* ── Surface ─────────────────────────────────────────────────────────────── */
.cx-surface {
  position: relative;
  border-radius: var(--cx-r-card);
  border: 1px solid var(--cx-edge);
  background: var(--cx-bg);
  box-shadow: var(--cx-out);
  transition: box-shadow .28s cubic-bezier(.22,1,.36,1),
              transform  .28s cubic-bezier(.22,1,.36,1);
}
/* Skeuomorphic grain — a real material, not a flat fill */
.cx-surface::after {
  content: "";
  position: absolute;
  inset: 0;
  border-radius: inherit;
  pointer-events: none;
  opacity: .03;
  background-image: ${GRAIN};
  mix-blend-mode: multiply;
}
/* Specular top-edge sheen (moulded plastic catching a light) */
.cx-surface::before {
  content: "";
  position: absolute;
  inset: 0 0 auto 0;
  height: 1px;
  border-radius: inherit;
  pointer-events: none;
  background: linear-gradient(90deg, transparent, rgba(255,255,255,.98), transparent);
}
.cx-surface > * { position: relative; z-index: 1; }

.cx-surface[data-flat="true"] {
  box-shadow:
    inset 0 1px 0 rgba(255,255,255,.9),
    0 1px 2px rgba(15,23,42,.05),
    0 8px 20px -10px rgba(15,23,42,.12);
}
@media (hover: hover) {
  .cx-surface[data-interactive="true"]:hover {
    transform: translateY(-2px);
    box-shadow: var(--cx-out-hover);
  }
}
.cx-surface[data-interactive="true"]:active { transform: translateY(0); }
.cx-surface[data-interactive="true"]:focus-visible {
  outline: 2px solid var(--teal);
  outline-offset: 3px;
}

/* Accent rail — tone-mapped hairline welded to the top edge */
.cx-rail::after {
  content: "";
  position: absolute;
  left: 14px; right: 14px; top: 0;
  height: 3px;
  border-radius: 0 0 3px 3px;
  background: var(--cx-rail-color, transparent);
  box-shadow: 0 1px 6px var(--cx-rail-glow, transparent);
  z-index: 2;
}

/* ── Well (inset recess) ─────────────────────────────────────────────────── */
.cx-well {
  position: relative;
  border-radius: 16px;
  border: 1px solid rgba(203,213,225,.55);
  background: var(--cx-bg-sunken);
  box-shadow: var(--cx-in-deep);
}

/* ── Button ──────────────────────────────────────────────────────────────── */
.cx-btn {
  position: relative;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: .45rem;
  border-radius: var(--cx-r-btn);
  font-family: inherit;
  font-weight: 650;
  letter-spacing: .01em;
  white-space: nowrap;
  cursor: pointer;
  user-select: none;
  border: 1px solid transparent;
  transition: box-shadow .18s ease, transform .1s ease, filter .18s ease;
}
.cx-btn:disabled { opacity: .48; cursor: not-allowed; }
.cx-btn:focus-visible { outline: 2px solid var(--teal); outline-offset: 2px; }

/* THE signature interaction: outset -> inset on press. It genuinely depresses. */
.cx-btn:not(:disabled):active {
  transform: translateY(1.5px) scale(.985);
}

/* primary — deep teal ramp. White on #0f766e = 5.5:1, on #115e59 = 7.6:1 (AA). */
.cx-btn-primary {
  color: #ffffff;
  background: linear-gradient(180deg, #0f766e 0%, #0d6d66 45%, #115e59 100%);
  border-color: rgba(6,78,73,.55);
  box-shadow:
    inset 0 1px 0 rgba(255,255,255,.38),
    inset 0 -3px 8px rgba(2,44,40,.42),
    0 2px 4px rgba(15,23,42,.14),
    0 12px 24px -10px rgba(13,148,136,.55),
    0 24px 48px -22px rgba(13,148,136,.40);
}
@media (hover: hover) { .cx-btn-primary:not(:disabled):hover { filter: brightness(1.08); } }
.cx-btn-primary:not(:disabled):active {
  box-shadow:
    inset 0 3px 8px rgba(2,44,40,.55),
    inset 0 -1px 0 rgba(255,255,255,.16),
    0 2px 6px -3px rgba(13,148,136,.45);
}

/* ghost — neumorphic extruded chip. #334155 on clay = 9.6:1 (AA). */
.cx-btn-ghost {
  color: #334155;
  background: var(--cx-bg);
  border-color: rgba(255,255,255,.8);
  box-shadow: var(--cx-raise);
}
@media (hover: hover) {
  .cx-btn-ghost:not(:disabled):hover { color: #0f172a; box-shadow: var(--cx-out-hover); }
}
.cx-btn-ghost:not(:disabled):active {
  color: #0f172a;
  box-shadow: var(--cx-in);
}

/* danger — white on #b91c1c = 6.5:1 (AA). */
.cx-btn-danger {
  color: #ffffff;
  background: linear-gradient(180deg, #dc2626 0%, #c81f1f 46%, #b91c1c 100%);
  border-color: rgba(127,29,29,.55);
  box-shadow:
    inset 0 1px 0 rgba(255,255,255,.34),
    inset 0 -3px 8px rgba(69,10,10,.42),
    0 2px 4px rgba(15,23,42,.14),
    0 12px 24px -10px rgba(220,38,38,.50),
    0 24px 48px -22px rgba(220,38,38,.36);
}
@media (hover: hover) { .cx-btn-danger:not(:disabled):hover { filter: brightness(1.07); } }
.cx-btn-danger:not(:disabled):active {
  box-shadow:
    inset 0 3px 8px rgba(69,10,10,.55),
    inset 0 -1px 0 rgba(255,255,255,.14),
    0 2px 6px -3px rgba(220,38,38,.40);
}

/* ── Field (inset) ───────────────────────────────────────────────────────── */
.cx-field {
  width: 100%;
  border-radius: var(--cx-r-field);
  border: 1px solid rgba(203,213,225,.62);
  background: var(--cx-bg-sunken);
  color: #0f172a;
  font-family: inherit;
  font-size: .875rem;
  font-weight: 500;
  outline: none;
  box-shadow: var(--cx-in);
  transition: box-shadow .18s ease, border-color .18s ease, background .18s ease;
}
.cx-field::placeholder { color: #64748b; }
.cx-field:hover:not(:disabled) { border-color: rgba(148,163,184,.85); }
.cx-field:focus { border-color: var(--teal); box-shadow: var(--cx-in-focus); }
.cx-field[aria-invalid="true"] { border-color: #dc2626; box-shadow: var(--cx-in-error); }
.cx-field:disabled { opacity: .55; cursor: not-allowed; }
.cx-select { appearance: none; -webkit-appearance: none; cursor: pointer; }

/* ── Toggle ──────────────────────────────────────────────────────────────── */
.cx-toggle {
  position: relative;
  flex-shrink: 0;
  border-radius: 999px;
  border: 1px solid rgba(203,213,225,.6);
  background: var(--cx-bg-sunken);
  box-shadow: var(--cx-in-deep);
  cursor: pointer;
  transition: background .22s ease, box-shadow .22s ease, border-color .22s ease;
}
.cx-toggle:disabled { opacity: .5; cursor: not-allowed; }
.cx-toggle:focus-visible { outline: 2px solid var(--teal); outline-offset: 2px; }
.cx-toggle[data-on="true"] {
  border-color: rgba(6,78,73,.5);
  background: linear-gradient(180deg, #0f766e 0%, #115e59 100%);
  box-shadow:
    inset 3px 3px 8px rgba(2,44,40,.5),
    inset -2px -2px 6px rgba(45,212,191,.25);
}
.cx-knob {
  position: absolute;
  top: 50%;
  border-radius: 999px;
  background: linear-gradient(180deg, #ffffff 0%, #e8eef6 100%);
  box-shadow:
    inset 0 1px 0 rgba(255,255,255,1),
    inset 0 -2px 4px rgba(148,163,184,.35),
    0 2px 4px rgba(15,23,42,.22),
    0 6px 12px -4px rgba(15,23,42,.28);
  transition: transform .26s cubic-bezier(.34,1.4,.64,1);
}

/* ── Badge ───────────────────────────────────────────────────────────────── */
.cx-badge {
  display: inline-flex;
  align-items: center;
  gap: .35rem;
  border-radius: 999px;
  padding: .3rem .68rem;
  font-size: .72rem;
  font-weight: 700;
  letter-spacing: .01em;
  white-space: nowrap;
  border: 1px solid var(--cx-badge-border);
  background: var(--cx-badge-bg);
  color: var(--cx-badge-fg);
  box-shadow:
    inset 0 1px 0 rgba(255,255,255,.9),
    inset 0 -2px 4px rgba(15,23,42,.05),
    0 1px 2px rgba(15,23,42,.07),
    0 4px 10px -4px rgba(15,23,42,.16);
}
.cx-badge-dot {
  width: 6px; height: 6px;
  border-radius: 999px;
  background: currentColor;
  box-shadow: 0 0 0 2px color-mix(in srgb, currentColor 18%, transparent);
}

/* ── Skeleton ────────────────────────────────────────────────────────────── */
.cx-skel {
  position: relative;
  overflow: hidden;
  border-radius: 10px;
  background: var(--cx-bg-sunken);
  box-shadow: var(--cx-in);
}
.cx-skel::after {
  content: "";
  position: absolute;
  inset: 0;
  transform: translateX(-100%);
  background: linear-gradient(90deg,
    transparent 0%, rgba(255,255,255,.72) 50%, transparent 100%);
  animation: cxShimmer 1.5s linear infinite;
}

/* ── Gauge ───────────────────────────────────────────────────────────────── */
.cx-gauge-arc {
  animation: cxSweep .9s cubic-bezier(.22,1,.36,1) both;
  transition: stroke-dashoffset .6s cubic-bezier(.22,1,.36,1);
}

@keyframes cxShimmer { to { transform: translateX(100%); } }
@keyframes cxSweep   { from { stroke-dashoffset: var(--cx-arc-len); } }

/* ── Reduced motion (WCAG 2.3.3) ─────────────────────────────────────────── */
@media (prefers-reduced-motion: reduce) {
  .cx-surface, .cx-btn, .cx-field, .cx-toggle, .cx-knob, .cx-gauge-arc {
    transition: none !important;
  }
  .cx-surface[data-interactive="true"]:hover { transform: none; }
  .cx-btn:not(:disabled):active { transform: none; }
  .cx-gauge-arc { animation: none; }
  .cx-skel::after { animation: none; opacity: .35; transform: none; }
}
`;

function ensureStyles(): void {
  if (typeof document === "undefined") return;
  if (document.getElementById(STYLE_ID)) return;
  const el = document.createElement("style");
  el.id = STYLE_ID;
  el.textContent = CSS;
  document.head.appendChild(el);
}
ensureStyles();

const cx = (...parts: Array<string | false | null | undefined>): string =>
  parts.filter(Boolean).join(" ");

/* ---------------------------------------------------------------------------
   Tones — every fg/bg pair below is >= 4.5:1. This is the table that keeps the
   system out of neumorphic mush; do not swap a fg for a lighter sibling.
   ------------------------------------------------------------------------ */

export type ClayTone = "good" | "warn" | "bad" | "info" | "neutral";

interface ToneSpec {
  fg: string;
  bg: string;
  border: string;
  accent: string; // saturated hue for rails, arcs, dots
}

const TONES: Record<ClayTone, ToneSpec> = {
  // #065f46 on #ecfdf5 = 7.4:1
  good: { fg: "#065f46", bg: "linear-gradient(180deg,#f0fdf7,#e3f9ee)", border: "rgba(16,185,129,.34)", accent: "#059669" },
  // #92400e on #fffbeb = 6.5:1
  warn: { fg: "#92400e", bg: "linear-gradient(180deg,#fffbeb,#fdf1d6)", border: "rgba(217,119,6,.34)", accent: "#d97706" },
  // #991b1b on #fef2f2 = 7.1:1
  bad: { fg: "#991b1b", bg: "linear-gradient(180deg,#fef4f4,#fde5e5)", border: "rgba(220,38,38,.32)", accent: "#dc2626" },
  // #1e40af on #eff6ff = 7.7:1
  info: { fg: "#1e40af", bg: "linear-gradient(180deg,#f2f7ff,#e4eefe)", border: "rgba(37,99,235,.32)", accent: "#2563eb" },
  // #334155 on #f8fafc = 9.9:1
  neutral: { fg: "#334155", bg: "linear-gradient(180deg,#fbfdff,#eef3f9)", border: "rgba(148,163,184,.36)", accent: "#64748b" },
};

export const clayTone = (tone: ClayTone): ToneSpec => TONES[tone];

/* ---------------------------------------------------------------------------
   ClaySurface — the puffy base object
   ------------------------------------------------------------------------ */

export interface ClaySurfaceProps extends HTMLAttributes<HTMLDivElement> {
  /** Softer, lower-lift shadow. Use when nesting a surface inside a surface. */
  flat?: boolean;
  /** Lifts + deepens the double shadow on hover; adds keyboard affordance. */
  interactive?: boolean;
  /** Welds a tone-mapped accent rail to the top edge. */
  rail?: ClayTone;
  /** Fill the parent flex track: `flex-1 min-h-0` + column layout. */
  fill?: boolean;
  as?: "div" | "section" | "article" | "aside";
}

export const ClaySurface = forwardRef<HTMLDivElement, ClaySurfaceProps>(
  function ClaySurface(
    { flat = false, interactive = false, rail, fill = false, as: Tag = "div", className = "", style, children, ...rest },
    ref,
  ) {
    const railStyle = rail
      ? ({
          ["--cx-rail-color" as string]: TONES[rail].accent,
          ["--cx-rail-glow" as string]: `${TONES[rail].accent}55`,
        } as CSSProperties)
      : undefined;

    return (
      <Tag
        ref={ref}
        data-flat={flat || undefined}
        data-interactive={interactive || undefined}
        tabIndex={interactive ? 0 : undefined}
        className={cx(
          "cx-surface",
          rail && "cx-rail",
          fill && "flex min-h-0 flex-1 flex-col",
          interactive && "cursor-pointer",
          className,
        )}
        style={{ ...railStyle, ...style }}
        {...rest}
      >
        {children}
      </Tag>
    );
  },
);

/* ---------------------------------------------------------------------------
   ClayCard — ClaySurface + header/body/footer composition
   ------------------------------------------------------------------------ */

export interface ClayCardProps extends Omit<ClaySurfaceProps, "title"> {
  title?: ReactNode;
  subtitle?: ReactNode;
  /** Right side of the header: buttons, badges, filters. */
  actions?: ReactNode;
  icon?: LucideIcon;
  footer?: ReactNode;
  /** Body scrolls internally instead of growing the page. Requires `fill`. */
  scrollBody?: boolean;
  /** Strip body padding — for a table or list that should bleed to the edges. */
  bleed?: boolean;
  dense?: boolean;
  bodyClassName?: string;
}

export const ClayCard = forwardRef<HTMLDivElement, ClayCardProps>(function ClayCard(
  {
    title,
    subtitle,
    actions,
    icon: Icon,
    footer,
    scrollBody = false,
    bleed = false,
    dense = false,
    bodyClassName = "",
    fill = false,
    children,
    className = "",
    ...rest
  },
  ref,
) {
  const pad = dense ? "px-3.5 py-3" : "px-5 py-4";
  const hasHeader = Boolean(title || subtitle || actions || Icon);

  return (
    <ClaySurface ref={ref} fill={fill} className={cx("overflow-hidden", className)} {...rest}>
      {hasHeader && (
        <header
          className={cx(
            "flex shrink-0 items-start justify-between gap-3 border-b border-slate-200/70",
            pad,
          )}
        >
          <div className="flex min-w-0 items-start gap-3">
            {Icon && (
              <span
                className="mt-0.5 grid size-8 shrink-0 place-items-center rounded-[10px] text-teal-700"
                style={{
                  background: "linear-gradient(180deg,#ffffff,#e9f1f8)",
                  boxShadow:
                    "inset 0 1px 0 rgba(255,255,255,1), 0 1px 2px rgba(15,23,42,.10), 0 5px 12px -6px rgba(15,23,42,.25)",
                }}
              >
                <Icon size={16} strokeWidth={2.2} aria-hidden />
              </span>
            )}
            <div className="min-w-0">
              {title && (
                <h3 className="truncate text-[0.9rem] font-bold tracking-[-0.01em] text-slate-900">
                  {title}
                </h3>
              )}
              {subtitle && (
                <p className="mt-0.5 truncate text-[0.75rem] font-medium text-slate-600">
                  {subtitle}
                </p>
              )}
            </div>
          </div>
          {actions && <div className="flex shrink-0 items-center gap-2">{actions}</div>}
        </header>
      )}

      <div
        className={cx(
          "min-w-0",
          fill && "flex min-h-0 flex-1 flex-col",
          scrollBody && "overflow-y-auto",
          !bleed && pad,
          bodyClassName,
        )}
      >
        {children}
      </div>

      {footer && (
        <footer
          className={cx(
            "shrink-0 border-t border-slate-200/70 bg-white/40",
            dense ? "px-3.5 py-2.5" : "px-5 py-3",
          )}
        >
          {footer}
        </footer>
      )}
    </ClaySurface>
  );
});

/* ---------------------------------------------------------------------------
   ClayButton — the signature press (outset -> inset)
   ------------------------------------------------------------------------ */

export type ClayButtonVariant = "primary" | "ghost" | "danger";
export type ClayButtonSize = "sm" | "md" | "lg";

export interface ClayButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ClayButtonVariant;
  size?: ClayButtonSize;
  icon?: LucideIcon;
  iconRight?: LucideIcon;
  loading?: boolean;
  block?: boolean;
}

const BTN_SIZE: Record<ClayButtonSize, string> = {
  sm: "px-3 py-1.5 text-[0.78rem]",
  md: "px-4 py-2.5 text-[0.83rem]",
  lg: "px-5 py-3 text-[0.9rem]",
};

const BTN_ICON: Record<ClayButtonSize, number> = { sm: 14, md: 15, lg: 17 };

export const ClayButton = forwardRef<HTMLButtonElement, ClayButtonProps>(function ClayButton(
  {
    variant = "ghost",
    size = "md",
    icon: Icon,
    iconRight: IconRight,
    loading = false,
    block = false,
    disabled,
    className = "",
    children,
    type = "button",
    ...rest
  },
  ref,
) {
  const s = BTN_ICON[size];
  return (
    <button
      ref={ref}
      type={type}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      className={cx("cx-btn", `cx-btn-${variant}`, BTN_SIZE[size], block && "w-full", className)}
      {...rest}
    >
      {loading ? (
        <Loader2 size={s} strokeWidth={2.4} className="animate-spin" aria-hidden />
      ) : (
        Icon && <Icon size={s} strokeWidth={2.3} aria-hidden />
      )}
      {children}
      {IconRight && !loading && <IconRight size={s} strokeWidth={2.3} aria-hidden />}
    </button>
  );
});

/* ---------------------------------------------------------------------------
   ClayWell — inset recess for tables / lists to sit inside
   ------------------------------------------------------------------------ */

export interface ClayWellProps extends HTMLAttributes<HTMLDivElement> {
  /** Fill the parent flex track. */
  fill?: boolean;
  /** Scroll internally rather than growing the page. */
  scroll?: boolean;
  padded?: boolean;
}

export const ClayWell = forwardRef<HTMLDivElement, ClayWellProps>(function ClayWell(
  { fill = false, scroll = false, padded = false, className = "", children, ...rest },
  ref,
) {
  return (
    <div
      ref={ref}
      className={cx(
        "cx-well",
        fill && "flex min-h-0 flex-1 flex-col",
        scroll && "min-h-0 overflow-auto",
        padded && "p-3",
        className,
      )}
      {...rest}
    >
      {children}
    </div>
  );
});

/* ---------------------------------------------------------------------------
   ClayStat — KPI tile with a pressed-in well for the value
   ------------------------------------------------------------------------ */

export type ClayTrend = "up" | "down" | "flat";

export interface ClayStatProps extends Omit<HTMLAttributes<HTMLDivElement>, "children"> {
  label: string;
  /** Pass a preformatted string. Render "—" yourself for unknowns; never invent one. */
  value: ReactNode;
  unit?: string;
  delta?: { value: string; trend: ClayTrend; /** Is this direction good? Defaults: up = good. */ good?: boolean };
  icon?: LucideIcon;
  tone?: ClayTone;
  /** Optional sparkline slot — e.g. <ClaySparkline points={…} />. */
  sparkline?: ReactNode;
  hint?: string;
}

const TREND_ICON: Record<ClayTrend, LucideIcon> = {
  up: TrendingUp,
  down: TrendingDown,
  flat: Minus,
};

export const ClayStat = forwardRef<HTMLDivElement, ClayStatProps>(function ClayStat(
  { label, value, unit, delta, icon: Icon, tone = "neutral", sparkline, hint, className = "", ...rest },
  ref,
) {
  const spec = TONES[tone];
  const TrendIcon = delta ? TREND_ICON[delta.trend] : null;

  // Semantics, not direction: a rising "late deliveries" is bad. Caller decides.
  const deltaTone: ClayTone =
    !delta || delta.trend === "flat"
      ? "neutral"
      : (delta.good ?? delta.trend === "up")
        ? "good"
        : "bad";
  const deltaSpec = TONES[deltaTone];

  return (
    <ClaySurface ref={ref} rail={tone} className={cx("flex flex-col p-4", className)} {...rest}>
      <div className="flex items-start justify-between gap-2">
        <p className="text-[0.68rem] font-bold uppercase tracking-[0.14em] text-slate-600">
          {label}
        </p>
        {Icon && (
          <span
            className="grid size-7 shrink-0 place-items-center rounded-lg"
            style={{
              color: spec.fg,
              background: spec.bg,
              boxShadow:
                "inset 0 1px 0 rgba(255,255,255,.95), 0 1px 2px rgba(15,23,42,.08), 0 5px 12px -6px rgba(15,23,42,.22)",
            }}
          >
            <Icon size={14} strokeWidth={2.3} aria-hidden />
          </span>
        )}
      </div>

      {/* Pressed-in well: the number is stamped INTO the clay. */}
      <div className="cx-well mt-3 flex items-end justify-between gap-3 rounded-[14px] px-3.5 py-2.5">
        <div className="flex min-w-0 items-baseline gap-1">
          <span className="truncate text-[1.7rem] font-extrabold leading-none tracking-[-0.03em] text-slate-950 tabular-nums">
            {value}
          </span>
          {unit && (
            <span className="shrink-0 text-[0.8rem] font-bold text-slate-600">{unit}</span>
          )}
        </div>
        {sparkline && <div className="shrink-0 opacity-90">{sparkline}</div>}
      </div>

      <div className="mt-2.5 flex min-h-[1.25rem] items-center gap-2">
        {delta && TrendIcon && (
          <span
            className="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[0.7rem] font-bold tabular-nums"
            style={{
              color: deltaSpec.fg,
              background: deltaSpec.bg,
              boxShadow: "inset 0 1px 0 rgba(255,255,255,.9), 0 1px 2px rgba(15,23,42,.07)",
            }}
          >
            <TrendIcon size={12} strokeWidth={2.6} aria-hidden />
            {delta.value}
          </span>
        )}
        {hint && <span className="truncate text-[0.7rem] font-medium text-slate-600">{hint}</span>}
      </div>
    </ClaySurface>
  );
});

/* ---------------------------------------------------------------------------
   ClaySparkline — dependency-free slot filler for ClayStat
   ------------------------------------------------------------------------ */

export interface ClaySparklineProps {
  points: number[];
  width?: number;
  height?: number;
  tone?: ClayTone;
}

export function ClaySparkline({ points, width = 64, height = 26, tone = "info" }: ClaySparklineProps) {
  const d = useMemo(() => {
    if (points.length < 2) return null;
    const min = Math.min(...points);
    const max = Math.max(...points);
    const span = max - min || 1;
    const step = width / (points.length - 1);
    return points
      .map((p, i) => {
        const x = i * step;
        const y = height - ((p - min) / span) * (height - 3) - 1.5;
        return `${i === 0 ? "M" : "L"}${x.toFixed(1)},${y.toFixed(1)}`;
      })
      .join(" ");
  }, [points, width, height]);

  if (!d) return null;

  return (
    <svg width={width} height={height} viewBox={`0 0 ${width} ${height}`} aria-hidden focusable="false">
      <path
        d={d}
        fill="none"
        stroke={TONES[tone].accent}
        strokeWidth={1.8}
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  );
}

/* ---------------------------------------------------------------------------
   ClayInput / ClaySelect — inset (pressed-in) fields
   ------------------------------------------------------------------------ */

interface FieldChromeProps {
  label?: string;
  hint?: string;
  error?: string;
  className?: string;
}

function FieldLabel({ htmlFor, children }: { htmlFor: string; children: ReactNode }) {
  return (
    <label
      htmlFor={htmlFor}
      className="mb-1.5 block text-[0.72rem] font-bold uppercase tracking-[0.1em] text-slate-700"
    >
      {children}
    </label>
  );
}

function FieldNote({ error, hint, id }: { error?: string; hint?: string; id: string }) {
  if (!error && !hint) return null;
  return (
    <p
      id={id}
      className={cx(
        "mt-1.5 text-[0.72rem] font-medium",
        error ? "text-red-700" : "text-slate-600",
      )}
      role={error ? "alert" : undefined}
    >
      {error ?? hint}
    </p>
  );
}

export interface ClayInputProps
  extends Omit<InputHTMLAttributes<HTMLInputElement>, "size">,
    FieldChromeProps {
  icon?: LucideIcon;
  /** Wrapper class; `className` targets the <input> itself. */
  wrapperClassName?: string;
}

export const ClayInput = forwardRef<HTMLInputElement, ClayInputProps>(function ClayInput(
  { label, hint, error, icon: Icon, className = "", wrapperClassName = "", id, ...rest },
  ref,
) {
  const auto = useId();
  const inputId = id ?? auto;
  const noteId = `${inputId}-note`;

  return (
    <div className={cx("w-full", wrapperClassName)}>
      {label && <FieldLabel htmlFor={inputId}>{label}</FieldLabel>}
      <div className="relative">
        {Icon && (
          <Icon
            size={15}
            strokeWidth={2.2}
            className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-slate-500"
            aria-hidden
          />
        )}
        <input
          ref={ref}
          id={inputId}
          aria-invalid={error ? true : undefined}
          aria-describedby={error || hint ? noteId : undefined}
          className={cx("cx-field py-2.5", Icon ? "pl-9 pr-3" : "px-3.5", className)}
          {...rest}
        />
      </div>
      <FieldNote error={error} hint={hint} id={noteId} />
    </div>
  );
});

export interface ClaySelectProps
  extends SelectHTMLAttributes<HTMLSelectElement>,
    FieldChromeProps {
  options?: Array<{ value: string; label: string }>;
  wrapperClassName?: string;
}

export const ClaySelect = forwardRef<HTMLSelectElement, ClaySelectProps>(function ClaySelect(
  { label, hint, error, options, className = "", wrapperClassName = "", id, children, ...rest },
  ref,
) {
  const auto = useId();
  const selectId = id ?? auto;
  const noteId = `${selectId}-note`;

  return (
    <div className={cx("w-full", wrapperClassName)}>
      {label && <FieldLabel htmlFor={selectId}>{label}</FieldLabel>}
      <div className="relative">
        <select
          ref={ref}
          id={selectId}
          aria-invalid={error ? true : undefined}
          aria-describedby={error || hint ? noteId : undefined}
          className={cx("cx-field cx-select py-2.5 pl-3.5 pr-9", className)}
          {...rest}
        >
          {options
            ? options.map((o) => (
                <option key={o.value} value={o.value}>
                  {o.label}
                </option>
              ))
            : children}
        </select>
        <ChevronDown
          size={15}
          strokeWidth={2.4}
          className="pointer-events-none absolute right-3 top-1/2 -translate-y-1/2 text-slate-500"
          aria-hidden
        />
      </div>
      <FieldNote error={error} hint={hint} id={noteId} />
    </div>
  );
});

/* ---------------------------------------------------------------------------
   ClayToggle — inset track, extruded knob
   ------------------------------------------------------------------------ */

export interface ClayToggleProps
  extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, "onChange" | "children"> {
  checked: boolean;
  onCheckedChange: (next: boolean) => void;
  label?: string;
  /** Visually hide the label but keep it for screen readers. */
  hideLabel?: boolean;
  size?: "sm" | "md";
}

export const ClayToggle = forwardRef<HTMLButtonElement, ClayToggleProps>(function ClayToggle(
  { checked, onCheckedChange, label, hideLabel = false, size = "md", disabled, className = "", ...rest },
  ref,
) {
  const dims =
    size === "sm"
      ? { w: 38, h: 22, knob: 16, pad: 3 }
      : { w: 48, h: 27, knob: 20, pad: 3.5 };
  const travel = dims.w - dims.knob - dims.pad * 2;

  const button = (
    <button
      ref={ref}
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={hideLabel ? label : undefined}
      disabled={disabled}
      onClick={() => onCheckedChange(!checked)}
      data-on={checked}
      className={cx("cx-toggle", !label && className)}
      style={{ width: dims.w, height: dims.h }}
      {...rest}
    >
      <span
        className="cx-knob"
        style={{
          width: dims.knob,
          height: dims.knob,
          left: dims.pad,
          transform: `translateY(-50%) translateX(${checked ? travel : 0}px)`,
        }}
        aria-hidden
      />
    </button>
  );

  if (!label || hideLabel) return button;

  return (
    <div className={cx("flex items-center gap-2.5", className)}>
      {button}
      <span className="text-[0.8rem] font-semibold text-slate-700">{label}</span>
    </div>
  );
});

/* ---------------------------------------------------------------------------
   ClayBadge — soft-extruded, tone-mapped pill
   ------------------------------------------------------------------------ */

export interface ClayBadgeProps extends HTMLAttributes<HTMLSpanElement> {
  tone?: ClayTone;
  icon?: LucideIcon;
  dot?: boolean;
}

export const ClayBadge = forwardRef<HTMLSpanElement, ClayBadgeProps>(function ClayBadge(
  { tone = "neutral", icon: Icon, dot = false, className = "", style, children, ...rest },
  ref,
) {
  const spec = TONES[tone];
  return (
    <span
      ref={ref}
      className={cx("cx-badge", className)}
      style={
        {
          ["--cx-badge-fg" as string]: spec.fg,
          ["--cx-badge-bg" as string]: spec.bg,
          ["--cx-badge-border" as string]: spec.border,
          ...style,
        } as CSSProperties
      }
      {...rest}
    >
      {dot && <span className="cx-badge-dot" aria-hidden />}
      {Icon && <Icon size={12} strokeWidth={2.6} aria-hidden />}
      {children}
    </span>
  );
});

/* ---------------------------------------------------------------------------
   ClayGauge — physical dial. score === null => "Not enough data".
   The backend returns NULL when a customer has too little delivery history.
   We render that honestly. We never fake a number.
   ------------------------------------------------------------------------ */

export interface ClayGaugeProps {
  /** 0–100, or null when the backend has insufficient history to score. */
  score: number | null;
  label?: string;
  /** Shown under the score, e.g. "vs. 82 fleet median". */
  caption?: string;
  size?: number;
  /** Explanation for the null state — tell the user WHY, not just that. */
  emptyHint?: string;
  className?: string;
}

const gaugeTone = (score: number): ClayTone =>
  score >= 85 ? "good" : score >= 70 ? "info" : score >= 50 ? "warn" : "bad";

export function ClayGauge({
  score,
  label = "Score",
  caption,
  size = 148,
  emptyHint = "Not enough delivery history to score this customer yet.",
  className = "",
}: ClayGaugeProps) {
  const stroke = Math.max(9, Math.round(size * 0.075));
  const r = (size - stroke) / 2 - 6;
  const c = size / 2;
  // 270° dial (skeuomorphic: leaves a gap at the bottom, like a real instrument)
  const SWEEP = 270;
  const circumference = 2 * Math.PI * r;
  const arcLen = (SWEEP / 360) * circumference;

  const hasScore = score !== null && Number.isFinite(score);
  const clamped = hasScore ? Math.min(100, Math.max(0, score)) : 0;
  const tone = hasScore ? gaugeTone(clamped) : "neutral";
  const accent = TONES[tone].accent;
  const offset = arcLen - (clamped / 100) * arcLen;

  // Instrument tick marks — the skeuomorphic tell.
  const ticks = useMemo(
    () =>
      Array.from({ length: 11 }, (_, i) => {
        const angle = (-225 + (SWEEP / 10) * i) * (Math.PI / 180);
        const inner = r - stroke / 2 - 4;
        const outer = inner - (i % 5 === 0 ? 7 : 4);
        return {
          key: i,
          x1: c + Math.cos(angle) * inner,
          y1: c + Math.sin(angle) * inner,
          x2: c + Math.cos(angle) * outer,
          y2: c + Math.sin(angle) * outer,
          major: i % 5 === 0,
        };
      }),
    [c, r, stroke],
  );

  return (
    <div className={cx("flex flex-col items-center", className)}>
      <div
        className="relative grid place-items-center rounded-full"
        style={{
          width: size,
          height: size,
          background: "var(--cx-bg-sunken)",
          boxShadow: "var(--cx-in-deep)",
        }}
      >
        <svg
          width={size}
          height={size}
          viewBox={`0 0 ${size} ${size}`}
          className="absolute inset-0"
          role="img"
          aria-label={
            hasScore ? `${label}: ${Math.round(clamped)} out of 100` : `${label}: not enough data`
          }
        >
          {/* Recessed track */}
          <circle
            cx={c}
            cy={c}
            r={r}
            fill="none"
            stroke="#d5dfeb"
            strokeWidth={stroke}
            strokeLinecap="round"
            strokeDasharray={`${arcLen} ${circumference}`}
            transform={`rotate(135 ${c} ${c})`}
          />
          {ticks.map((t) => (
            <line
              key={t.key}
              x1={t.x1}
              y1={t.y1}
              x2={t.x2}
              y2={t.y2}
              stroke={t.major ? "#94a3b8" : "#cbd5e1"}
              strokeWidth={t.major ? 1.6 : 1}
              strokeLinecap="round"
            />
          ))}
          {/* Needle arc — only when there is a real score */}
          {hasScore && (
            <circle
              className="cx-gauge-arc"
              cx={c}
              cy={c}
              r={r}
              fill="none"
              stroke={accent}
              strokeWidth={stroke}
              strokeLinecap="round"
              strokeDasharray={`${arcLen} ${circumference}`}
              strokeDashoffset={offset}
              transform={`rotate(135 ${c} ${c})`}
              style={
                {
                  ["--cx-arc-len" as string]: `${arcLen}`,
                  filter: `drop-shadow(0 2px 5px ${accent}66)`,
                } as CSSProperties
              }
            />
          )}
        </svg>

        {/* Raised center hub — clay boss sitting inside the dial well */}
        <div
          className="relative grid place-items-center rounded-full text-center"
          style={{
            width: size * 0.6,
            height: size * 0.6,
            background: "var(--cx-bg)",
            boxShadow:
              "inset 0 2px 0 rgba(255,255,255,.95), 0 2px 4px rgba(15,23,42,.10), 0 12px 24px -10px rgba(15,23,42,.28)",
          }}
        >
          {hasScore ? (
            <>
              <span className="text-[1.55rem] font-extrabold leading-none tracking-[-0.03em] text-slate-950 tabular-nums">
                {Math.round(clamped)}
              </span>
              <span className="mt-1 text-[0.6rem] font-bold uppercase tracking-[0.12em] text-slate-600">
                {label}
              </span>
            </>
          ) : (
            <span className="px-2 text-[0.62rem] font-bold uppercase leading-tight tracking-[0.06em] text-slate-600">
              No score
            </span>
          )}
        </div>
      </div>

      {hasScore ? (
        caption && (
          <p className="mt-2.5 text-center text-[0.72rem] font-medium text-slate-600">{caption}</p>
        )
      ) : (
        <div className="mt-2.5 max-w-[16rem] text-center">
          <ClayBadge tone="neutral">Not enough data</ClayBadge>
          <p className="mt-1.5 text-[0.72rem] font-medium leading-snug text-slate-600">
            {emptyHint}
          </p>
        </div>
      )}
    </div>
  );
}

/* ---------------------------------------------------------------------------
   ClaySkeleton — loading shimmer, same material as the surfaces
   ------------------------------------------------------------------------ */

export interface ClaySkeletonProps extends HTMLAttributes<HTMLDivElement> {
  variant?: "text" | "block" | "circle";
  /** Number of stacked lines (text variant only). */
  lines?: number;
  width?: number | string;
  height?: number | string;
  radius?: number | string;
}

export function ClaySkeleton({
  variant = "block",
  lines = 1,
  width,
  height,
  radius,
  className = "",
  style,
  ...rest
}: ClaySkeletonProps) {
  const base: CSSProperties = {
    width: width ?? (variant === "circle" ? 40 : "100%"),
    height:
      height ?? (variant === "circle" ? 40 : variant === "text" ? "0.75rem" : "100%"),
    borderRadius: radius ?? (variant === "circle" ? 999 : variant === "text" ? 6 : 12),
    ...style,
  };

  if (variant === "text" && lines > 1) {
    return (
      <div className={cx("flex w-full flex-col gap-2", className)} aria-hidden {...rest}>
        {Array.from({ length: lines }, (_, i) => (
          <div
            key={i}
            className="cx-skel"
            style={{ ...base, width: i === lines - 1 ? "62%" : base.width }}
          />
        ))}
      </div>
    );
  }

  return <div className={cx("cx-skel", className)} style={base} aria-hidden {...rest} />;
}

/** Convenience: a skeleton shaped like a ClayStat, for KPI-row loading. */
export function ClayStatSkeleton({ className = "" }: { className?: string }) {
  return (
    <ClaySurface className={cx("flex flex-col p-4", className)}>
      <ClaySkeleton variant="text" width="45%" />
      <div className="cx-well mt-3 rounded-[14px] px-3.5 py-2.5">
        <ClaySkeleton variant="text" height="1.7rem" width="60%" radius={8} />
      </div>
      <ClaySkeleton variant="text" width="35%" className="mt-2.5" />
    </ClaySurface>
  );
}
