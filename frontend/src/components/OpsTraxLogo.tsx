import { useId } from "react";

/**
 * OpsTrax 3-D isometric-cube logo.
 *
 * Hexagon (flat-top, 32 × 32 viewBox, circumradius 14, center 16,16):
 *   vertices:  (16,2) · (28.1,9) · (28.1,23) · (16,30) · (3.9,23) · (3.9,9)
 * Split into three rhombus faces (classic cube-in-hexagon illusion):
 *   top   — (16,2) → (28.1,9) → (16,16) → (3.9,9)
 *   left  — (3.9,9) → (16,16) → (16,30) → (3.9,23)
 *   right — (28.1,9) → (28.1,23) → (16,30) → (16,16)
 * Fleet route nodes are stamped on the top face.
 */
export function OpsTraxLogo({ size = 36 }: { size?: number }) {
  const uid = useId().replace(/:/g, "");

  const top   = `${uid}T`;
  const left  = `${uid}L`;
  const right = `${uid}R`;
  const shine = `${uid}S`;
  const drop  = `${uid}D`;

  return (
    <svg
      viewBox="0 0 32 32"
      width={size}
      height={size}
      fill="none"
      aria-label="OpsTrax"
      role="img"
    >
      <defs>
        {/* Top face — bright lit-from-above teal */}
        <linearGradient id={top} x1="0.2" y1="0" x2="0.8" y2="1">
          <stop offset="0%"   stopColor="#5eead4" />
          <stop offset="100%" stopColor="#0d9488" />
        </linearGradient>

        {/* Left face — mid teal */}
        <linearGradient id={left} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%"   stopColor="#0d9488" />
          <stop offset="100%" stopColor="#0f4f47" />
        </linearGradient>

        {/* Right face — deep shadow */}
        <linearGradient id={right} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%"   stopColor="#0a6660" />
          <stop offset="100%" stopColor="#021a14" />
        </linearGradient>

        {/* Top-face inner shine */}
        <radialGradient id={shine} cx="40%" cy="30%" r="60%">
          <stop offset="0%"   stopColor="rgba(255,255,255,0.22)" />
          <stop offset="100%" stopColor="rgba(255,255,255,0)" />
        </radialGradient>

        {/* Subtle drop shadow */}
        <filter id={drop} x="-20%" y="-20%" width="140%" height="150%">
          <feDropShadow dx="0" dy="2" stdDeviation="2.5" floodColor="#0d9488" floodOpacity="0.35" />
        </filter>
      </defs>

      <g filter={`url(#${drop})`}>
        {/* ── Three isometric faces ── */}
        {/* Top face */}
        <polygon
          points="16,2 28.1,9 16,16 3.9,9"
          fill={`url(#${top})`}
        />
        {/* Left face */}
        <polygon
          points="3.9,9 16,16 16,30 3.9,23"
          fill={`url(#${left})`}
        />
        {/* Right face */}
        <polygon
          points="28.1,9 28.1,23 16,30 16,16"
          fill={`url(#${right})`}
        />

        {/* ── Edge highlights (top-left and top-right ridge) ── */}
        <line x1="16" y1="2" x2="3.9"  y2="9"  stroke="rgba(255,255,255,0.28)" strokeWidth="0.6" />
        <line x1="16" y1="2" x2="28.1" y2="9"  stroke="rgba(255,255,255,0.18)" strokeWidth="0.6" />
        {/* Center vertical edge */}
        <line x1="16" y1="16" x2="16" y2="2"   stroke="rgba(255,255,255,0.12)" strokeWidth="0.5" />

        {/* ── Top-face inner shine ── */}
        <polygon
          points="16,2 28.1,9 16,16 3.9,9"
          fill={`url(#${shine})`}
        />

        {/* ── Fleet route nodes on top face ── */}
        {/* Three vehicle tracking nodes */}
        <circle cx="10.5" cy="10.2" r="1.5" fill="rgba(255,255,255,0.82)" />
        <circle cx="16"   cy="6"    r="1.5" fill="rgba(255,255,255,0.82)" />
        <circle cx="21.5" cy="10.2" r="1.5" fill="rgba(255,255,255,0.82)" />
        {/* Connecting route lines */}
        <line x1="10.5" y1="10.2" x2="16"   y2="6"    stroke="rgba(255,255,255,0.5)" strokeWidth="0.9" strokeLinecap="round" />
        <line x1="16"   y1="6"    x2="21.5" y2="10.2" stroke="rgba(255,255,255,0.5)" strokeWidth="0.9" strokeLinecap="round" />
        {/* Subtle mid-route pulse dot */}
        <circle cx="13.2" cy="8.1" r="0.8" fill="rgba(255,255,255,0.38)" />
        <circle cx="18.7" cy="8.1" r="0.8" fill="rgba(255,255,255,0.38)" />

        {/* ── Face-edge definition lines ── */}
        {/* Vertical center rib */}
        <line x1="16" y1="16" x2="16" y2="30" stroke="rgba(0,0,0,0.18)" strokeWidth="0.4" />
        {/* Bottom-left edge */}
        <line x1="3.9" y1="23" x2="16" y2="30" stroke="rgba(0,0,0,0.12)" strokeWidth="0.4" />
        {/* Bottom-right edge */}
        <line x1="28.1" y1="23" x2="16" y2="30" stroke="rgba(0,0,0,0.1)" strokeWidth="0.4" />
      </g>
    </svg>
  );
}
