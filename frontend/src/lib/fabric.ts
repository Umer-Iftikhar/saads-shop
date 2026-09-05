/**
 * Fabric is drawn in CSS, not photographed.
 *
 * The shop has no product photography yet, so a cloth is rendered from its
 * colour and weave. This module is the single seam where that happens: the
 * storefront, the set builder and the shop panel all call it, so a given cloth
 * looks identical everywhere it appears. When real photographs arrive they
 * replace the gradient here and nowhere else.
 *
 * The three weaves and their geometry come straight from the design prototype.
 */

export type Weave = 'Woven' | 'Striped' | 'Floral';

/** The cream the weaves are cut with — the design system's background. */
const CREAM = '#f5ead8';

/**
 * A CSS `background` value for a cloth.
 *
 * `colorValue` is whatever the database holds: a hex string, or `oklch(...)`
 * for Gold and Plum. It is interpolated as-is — converting the OKLCH values to
 * hex would shift them, and browsers read `oklch()` natively.
 */
export function fabricBackground(colorValue: string | null | undefined, weave: string | null | undefined): string {
  const c = colorValue?.trim() || 'var(--color-neutral-400)';

  switch (weave) {
    case 'Striped':
      return `repeating-linear-gradient(100deg, ${c} 0 12px, ${CREAM} 12px 20px)`;

    case 'Floral':
      return `radial-gradient(circle at 9px 9px, ${CREAM} 3px, transparent 3.5px) 0 0/18px 18px, ${c}`;

    case 'Woven':
    default:
      // color-mix keeps the second tone tied to the first, so a new cloth needs
      // one colour rather than a hand-picked pair.
      return `repeating-linear-gradient(45deg, ${c} 0 9px, color-mix(in srgb, ${c} 74%, ${CREAM}) 9px 18px)`;
  }
}

/**
 * An accessible name for a swatch control.
 *
 * The prototype distinguished the selected swatch by colour alone. Colour is
 * not available to a screen-reader user and not reliable for a colour-blind
 * one, so selection is announced here and marked with a check glyph in the UI.
 */
export function fabricLabel(name: string, weave: string | null | undefined, selected = false): string {
  const woven = weave && weave !== 'Woven' ? `${weave.toLowerCase()} ` : '';
  return `${name}${woven ? `, ${woven.trim()}` : ''}${selected ? ' (selected)' : ''}`;
}
