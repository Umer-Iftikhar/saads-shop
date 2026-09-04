# Design system — "Organic"

Lifted verbatim from the Claude Design handoff
(`docs/design-handoff/project/_ds/organic-.../styles.css`). The prototype is the reference;
these tokens are the contract between it and the built app. Retune here, not in components.

## Palette

Warm cream ground, terracotta accent, sage second accent. Ramps were generated in OKLCH on
one shared lightness scale, so step 500 of any role reads at the same visual weight as step
500 of another.

| Role | Base | Ramp |
| --- | --- | --- |
| Background | `#f5ead8` | — |
| Surface | `#ebddc5` | — |
| Text | `#201e1d` | — |
| Accent (terracotta) | `#c67139` | `100 #fff2eb` · `300 #ffc6a5` · `500 #d67f48` · `700 #8c491a` · `900 #402310` |
| Accent-2 (sage) | `#7a8a5e` | `100 #f0fae1` · `300 #ccdbb2` · `500 #8fa073` · `700 #56633f` · `900 #272e1b` |
| Neutral | — | `100 #f9f4ed` · `300 #dcd3c4` · `500 #a19786` · `700 #645c50` · `900 #2e2b25` |
| Divider | `color-mix(in srgb, #201e1d 16%, transparent)` | |

## Type

| Token | Value |
| --- | --- |
| `--font-heading` | `"Caprasimo", system-ui, sans-serif` — weight 400, the only weight it has |
| `--font-body` | `"Figtree", system-ui, sans-serif` — 400 / 600 / 700 |

Scale: h1 42 · h2 32 · h3 25 · h4 20 · h5 16 · h6 13 (uppercase, `0.08em` tracking).
Body 15px / 1.55. Headings sit at `line-height: 1.12`, `letter-spacing: -0.015em`.

Hero type in the design goes far past the scale on purpose — 84px on the home hero, 70px on
the confirmation, 60px on listing headers. That oversized display type was an explicit ask;
keep it.

## Spacing, radius, elevation

```
--space-1 4.4px   --space-2 8.8px   --space-3 13.2px
--space-4 17.6px  --space-6 26.4px  --space-8 35.2px

--radius-sm 8px   --radius-md 16px  --radius-lg 28px

--shadow-sm  0 1px 2px   rgba(46,43,37,.14)
--shadow-md  0 3px 10px  rgba(46,43,37,.16)
--shadow-lg  0 12px 32px rgba(46,43,37,.22)
```

The system runs a **rounded frame**: cards and dialogs take `radius-lg × 1.15`, and buttons,
tags, segmented controls and inputs all go fully pill (`999px`). Almost every control in the
design is a pill — that is the strongest single visual signature, so do not square anything
off.

## Component classes

`.btn` (`-primary` / `-secondary` / `-ghost` / `-icon` / `-block`), `.card` (+ `.card-kicker`,
`.card-title`, `.card-body`, `.card-meta`), `.elev-sm|md|lg`, `.tag` (`-accent` / `-accent-2`
/ `-neutral` / `-outline`), `.field` + `.input`, `.radio`, `.seg` + `.seg-opt`, `.table`,
`.nav`, `.dialog`.

`.washed` is the fabric filter — `saturate(.6) contrast(.85) brightness(1.1) opacity(.94)`.
Every swatch surface in the design wears it, which is what stops the woven gradients looking
like flat colour blocks. Real photographs will want it too.

## Fabric swatches

Fabric is drawn in CSS, not photographed. Six cloth colours — Terracotta, Sage, Cream, Clay,
Gold, Plum — rendered in one of three weaves:

```ts
woven:  repeating-linear-gradient(45deg,  C 0 9px, mix(C 74%, cream) 9px 18px)
striped: repeating-linear-gradient(100deg, C 0 12px, cream 12px 20px)
floral:  radial-gradient(circle at 9px 9px, cream 3px, transparent 3.5px) 0 0/18px 18px, C
```

This lives in one module (`fabric.ts`) shared by the storefront, the set builder and the
shop panel, so a swatch looks identical everywhere it appears. When real photos arrive they
replace the gradient at this single seam.

## Logo

A terracotta disc with a scalloped cream hem (the cloth), a Caprasimo "S", and a sage dot at
the upper right. Rendered as inline SVG at 44px in the storefront nav and 36px in the panel
sidebar. Kept as a component, never a raster file, so it stays crisp and recolourable.

## Layout widths

The prototype frames are fixed-width: **1280px** for storefront pages, **1440px** for the
shop panel with a **258px** sidebar. Those are canvas frames, not a responsive spec — the
built app treats 1280/1440 as the desktop target and reflows below it. The panel sidebar
collapses to icons under 1100px; storefront grids step 4 → 2 → 1 column.

## Accessibility

The system ships `:focus-visible { outline: 2px solid var(--color-accent) }` and it is kept
— no `outline: none` without a replacement. Two things in the prototype need fixing on the
way to production, and are treated as bugs rather than design:

- Fabric swatches are `div`s with `onClick`. They become real `<button>`s with accessible
  names ("Terracotta, woven") and arrow-key roving focus within a group.
- Colour alone distinguishes selected swatches and order statuses. Selection also gets a
  check mark; statuses keep their text label alongside the colour.

Sage `#7a8a5e` on cream `#f5ead8` is about 3.1:1 — fine for large display type, **not** for
body text. Body copy on cream uses `--color-neutral-700` (`#645c50`, ≈ 7:1).
