# Frontend — Saad's Shop

React 19 + TypeScript on Vite. The storefront: six customer-facing screens built
from the Claude Design handoff.

The design tokens are ported verbatim from the prototype into
[`src/styles/tokens.css`](src/styles/tokens.css) — that file is the contract
between the design and the app. Retune there, never in a component.

## Running it

The API must be up first (see [`../backend/README.md`](../backend/README.md)).

```bash
npm install
npm run dev      # http://localhost:5173
```

Vite **proxies `/api` to `http://127.0.0.1:5199`**, so the browser sees one
origin. That is deliberate: it keeps the refresh cookie `SameSite=Strict` in
development as well as production, rather than relaxing the setting just to make
local development work.

| Script | Does |
| --- | --- |
| `npm run dev` | Dev server with HMR |
| `npm run build` | Production bundle |
| `npm run typecheck` | `tsc -b` |
| `npm run lint` | oxlint |
| `npm run check` | all three |

## Layout

```
src/
├── pages/       one file per screen from the design
├── components/  ShopNav, ProductCard, Swatch, Field, Feedback, Logo
├── hooks/       TanStack Query hooks, one per resource
├── lib/         api client, fabric rendering, formatting, validation
├── state/       the cart (context + localStorage)
├── types/       TypeScript mirrors of the API's shapes
└── styles/      tokens.css (from the design) + base.css (components)
```

## Things worth knowing

**Fabric is drawn, not photographed.** `lib/fabric.ts` turns a colour and a
weave into a CSS gradient, and it is the single seam where that happens. When
real photographs arrive they replace the gradient there and nowhere else.

**The cart never stores a price it trusts.** It keeps what was chosen; prices
shown come from the catalogue, and the binding total is the one the checkout
procedure computes under lock. The cart's own arithmetic is labelled an
estimate on screen for that reason.

**Validation is for feedback, not safety.** `lib/validation.ts` mirrors the
server's rules so a customer learns their phone number is wrong before a round
trip. Nothing downstream trusts it — the API and the stored procedures both
re-check. Server-side field errors are folded into the same shape the local
validators produce, so one rendering path handles both.

**No `setState` in an effect to seed state from data.** The product page and the
set builder derive their default swatch during render instead: seeding from an
effect costs a second render on every load and briefly shows the wrong cloth.

## Two defects in the prototype, fixed rather than copied

The design was an HTML mockup, and two of its interaction patterns do not
survive contact with real users:

1. **Swatches were `div`s with `onClick`** — unreachable by keyboard, silent to a
   screen reader. They are now real `<button>`s in a `radiogroup`, with
   accessible names ("Terracotta, selected") and arrow-key navigation within the
   group.
2. **Selection was signalled by colour alone.** A ring *and* a check mark now
   mark the chosen swatch, and `aria-pressed` carries it to assistive tech.

Body copy also moved off sage-on-cream (≈3.1:1) onto `--color-neutral-700`
(≈7:1). Sage is kept for large display type, where the contrast is adequate.

## Verified in a real browser

Driven with Playwright against the live API and SQL Server:

| Check | Result |
| --- | --- |
| Home: hero, banner from settings, featured products, `Rs 18,500` formatting | ✅ |
| Design tokens applied — cream ground, Caprasimo headings, pill buttons | ✅ |
| Swatch click updates the cloth, the `aria-label` and `aria-pressed` | ✅ |
| Set builder: three pickers, room described to screen readers | ✅ |
| **Double → King reprices Rs 7,400 → Rs 10,000 via the server** | ✅ |
| Cart shows the added item; only enabled payment methods appear (no Card) | ✅ |
| Empty checkout blocked client-side with three field-level errors | ✅ |
| No horizontal overflow at 1280 / 1100 / 900 / 390 px | ✅ |
| Every touch target ≥ 44px on a phone | ✅ |
| Skip link is the first tab stop | ✅ |
| `typecheck`, `lint`, `build` | clean, 0 warnings |

## Not here yet

The shop panel (eight admin screens) and the staff sign-in screens are the next
phase. `VITE_API_BASE_URL` overrides the API base if you are not using the
proxy.
