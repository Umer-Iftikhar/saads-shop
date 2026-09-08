# Frontend — Saad's Shop

React 19 + TypeScript on Vite, styled with Tailwind v4. Six customer-facing
screens from the Claude Design handoff, plus the eight-screen shop panel and the
staff sign-in screens.

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
| `npm test` | Vitest, once |
| `npm run test:watch` | Vitest, watching |
| `npm run check` | typecheck, lint, test, build |

The storefront is at `/`; the staff panel is at `/shop-panel`. The first Owner
comes from [`../database/seed/04_owner.sql`](../database/seed/04_owner.sql) —
sign in, enrol an authenticator, change the password.

## Layout

```
src/
├── pages/       one file per screen; pages/admin/ is the shop panel
├── components/  ShopNav, ProductCard, Swatch, Field, Feedback, Logo
│   └── admin/   the panel's shell, sidebar and status tags
├── hooks/       TanStack Query hooks, one per resource
├── lib/         api client, fabric rendering, formatting, validation
├── state/       the cart (context + localStorage), the staff session
├── types/       TypeScript mirrors of the API's shapes
└── styles/      theme.css (the design system) + app.css (base + primitives)
```

## The design system is the Tailwind theme

[`src/styles/theme.css`](src/styles/theme.css) is the contract between the
prototype and the app. Every token from the handoff is a Tailwind `@theme`
variable, so it exists as a CSS custom property *and* generates the matching
utilities — `--color-accent-600` gives `bg-accent-600`, `text-accent-600`,
`border-accent-600`. There is no second copy of the palette in a config file to
drift out of step with it.

Three consequences worth knowing:

- **Tailwind's own palette is switched off** (`--color-*: initial`). `bg-blue-500`
  is not a class that silently works; only the shop's colours are nameable.
- **The spacing scale is the design's 4.4px step**, not Tailwind's 4px. `p-4` is
  the handoff's `--space-4` exactly.
- **The breakpoints are the design's**, named for what actually changes shape:
  `2xl:` is "wide enough for the hero's two columns", not 1536px.

[`app.css`](src/styles/app.css) holds element defaults and the handful of
primitives that carry several states each — `.btn`, `.chip`, `.input`, `.card`,
`.tag`, `.table`, the toggle. Layout lives in the components as utilities: a grid
that changes shape at three breakpoints reads better beside the markup it shapes
than in a file three directories away.

## Things worth knowing

**Fabric is drawn, not photographed.** `lib/fabric.ts` turns a colour and a
weave into a CSS gradient, and it is the single seam where that happens. When
real photographs arrive they replace the gradient there and nowhere else. It is
also the only place an inline `style` survives — a generated gradient cannot be
a utility class.

**The access token is never stored.** It lives in memory; the refresh token is
an HttpOnly cookie the browser sends on its own. A reload recovers the session
by calling `/auth/refresh`, so an XSS bug has nothing to read.

**Only one refresh at a time.** Refresh tokens rotate and the server treats a
replayed one as theft, revoking the whole family. Two refreshes racing therefore
end the session rather than merely duplicating work — and they race easily:
StrictMode runs the mount effect twice, and several failing requests would each
want to refresh. `state/auth.tsx` keeps the in-flight promise and everyone waits
on it. This was found by reloading the panel twice in a real browser, not by
reading the code.

**The cart never stores a price it trusts.** It keeps what was chosen; prices
shown come from the catalogue, and the binding total is the one the checkout
procedure computes under lock. The cart's own arithmetic is labelled an estimate
on screen for that reason.

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

The panel's settings toggle got the same treatment: a real checkbox drives it,
so it is keyboard-operable and announced as a checkbox. Body copy also moved off
sage-on-cream (≈3.1:1) onto `--color-neutral-700` (≈7:1). Sage is kept for large
display type, where the contrast is adequate.

## Verified in a real browser

Driven with Playwright against the live API and SQL Server — including a full
sign-in: password, then a TOTP the test computes from the enrolment key.

| Check | Result |
| --- | --- |
| Home: hero, banner from settings, featured products, `Rs 18,500` formatting | ✅ |
| Theme applied — cream ground, terracotta pill buttons, display headings | ✅ |
| Swatch click updates the cloth, the `aria-label` and `aria-pressed` | ✅ |
| **Double → King reprices Rs 7,400 → Rs 10,000 via the server** | ✅ |
| Empty checkout blocked client-side with three field-level errors | ✅ |
| Visiting `/shop-panel` signed out lands on sign-in | ✅ |
| An account with no authenticator is sent to enrolment, not a code prompt | ✅ |
| **Password + TOTP opens the panel; 10 recovery codes shown once** | ✅ |
| All eight panel screens render, and the order detail opens a real order | ✅ |
| A backwards date range is refused before any request is sent | ✅ |
| The sidebar is a drawer on a phone, and hidden from the tab order until opened | ✅ |
| No horizontal overflow at 1280 / 1100 / 900 / 390 px | ✅ |
| Every touch target ≥ 44px under a coarse pointer | ✅ |
| Sign out returns to sign-in | ✅ |
| `typecheck`, `lint`, `build` | clean, 0 warnings |

Two bugs came out of that run and are fixed: the refresh race above, and — in
the backend — a JWT claim-map default that made *every* 2FA sign-in answer "that
sign-in attempt has expired". See [`../backend/README.md`](../backend/README.md).

The web fonts are fetched from Google Fonts, which this build container cannot
reach; the headings therefore fell back to Georgia during verification. The font
stack itself is unchanged and loads normally on a machine with network access.

## Tests

```bash
npm test                          # 288 tests, ~7s
npm test -- src/state/cart        # one file
```

Vitest with Testing Library and jsdom. Nine files, and they are queried the way
a customer meets the page — by role, label and accessible name — so a test fails
when the shop breaks, not when a class name changes.

| File | What is asserted |
| --- | --- |
| `lib/validation.test.ts` | Every rule the panel and the checkout apply, including the date range the server's `[DateRange]` checks again |
| `lib/format.test.ts` | Rupees, dates, phone grouping, `wa.me` links, and the drawn cloth |
| `lib/api.test.ts` | The base URL, the bearer header, when cookies travel, problem+json into `ApiError`, and that a network failure says something a shopper can act on |
| `state/cart.test.tsx` | Merging by product + cloth + size, the quantity and line caps, what is sent to the server (no prices), and surviving hand-edited `localStorage` |
| `state/auth.test.tsx` | Recovering a session on load, and the shared in-flight promise that stops two refreshes racing |
| `components/components.test.tsx` | `Field`'s label/error wiring, the feedback states' live regions, and the swatch group's arrow-key navigation |
| `components/navigation.test.tsx` | The panel gate, Settings hidden from staff, and the cart's spoken count |
| `pages/admin/Orders.test.tsx` | The date filter — every refusal asserts that nothing was sent, not merely that a message appeared |
| `pages/admin/SignIn.test.tsx` | Both steps: the password buys a challenge token and never a session, an unenrolled account is sent to setup, and a mistyped code does not cost the challenge |

Two assertions are deliberately loose. Dates are matched by shape rather than by
exact string, because the month abbreviation comes from the platform's ICU data
("Sep" in a browser, "Sept" in Node) and pinning one spelling would fail on the
other with nothing actually wrong. And `validateDateRange` reports *both*
future-date errors where the server reports one — a deliberate difference,
noted where it is tested, since being stricter on the client is safe.

## Not here yet

Photography is optional, not absent: a product with no photo is drawn in CSS from its
cloth, and one with a photo shows it. Upload from the product editor.
`VITE_API_BASE_URL` overrides the API base if you are not using the proxy.
