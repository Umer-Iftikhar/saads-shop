/**
 * Saad's Shop mark: a terracotta disc with a scalloped cream hem — the cloth —
 * a Caprasimo "S", and a sage dot at the upper right.
 *
 * Inline SVG rather than a raster file so it stays crisp at any size and can be
 * recoloured from tokens. The colours are literal here because the mark is
 * fixed: it does not change with the theme.
 */
export function Logo({ size = 44 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 44 44" role="img" aria-label="Saad's Shop">
      <circle cx="22" cy="22" r="21" fill="#c67139" />
      {/* the hem — a bolt of cloth draped across the disc */}
      <path d="M4 30c4 0 4-4 8-4s4 4 8 4 4-4 8-4 4 4 8 4v9H4z" fill="#f5ead8" />
      <path
        d="M4 30c4 0 4-4 8-4s4 4 8 4 4-4 8-4 4 4 8 4"
        stroke="#8c491a"
        strokeWidth="1.5"
        fill="none"
        opacity="0.35"
      />
      <text
        x="22" y="24" textAnchor="middle"
        fontFamily="Caprasimo, Georgia, serif" fontSize="21" fill="#f5ead8"
      >
        S
      </text>
      <circle cx="35" cy="9" r="4" fill="#7a8a5e" />
    </svg>
  );
}
