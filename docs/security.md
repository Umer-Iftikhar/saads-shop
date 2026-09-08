# Security

Staff authenticate; customers do not (yet). The storefront is anonymous — browsing,
building a set and placing a cash-on-delivery order need no account, exactly as the design
shows. The schema carries a nullable `CustomerUserId` on `Orders` so customer accounts can
be added later without a migration that rewrites history.

## Authentication

**ASP.NET Core Identity** owns credentials; **JWT** carries the session.

| Piece | Setting |
| --- | --- |
| Password hashing | Identity default (PBKDF2-HMAC-SHA512, 210 000 iterations) |
| Password policy | ≥ 12 chars, upper + lower + digit + symbol, checked against a common-password list |
| Lockout | 5 failed attempts → 15 min lockout, on by default, applies to unknown users too (so failures are indistinguishable) |
| Access token | JWT, **15 minutes**, HS256 over a ≥ 256-bit key from configuration |
| Refresh token | 32 bytes from `RandomNumberGenerator`, **14 days**, stored **hashed** (SHA-256) — a database leak yields no usable token |
| 2FA | TOTP (RFC 6238), 30 s step, ±1 window skew, required for every staff account |
| External login | Google OAuth 2.0 with PKCE |

### JWT claims

`sub` (user id), `email`, `name`, `role` (`Owner` / `Staff`), `jti`, `iat`, `exp`, `iss`,
`aud`, and `amr` recording how the user actually authenticated (`pwd`, `mfa`, `google`).
Authorization policies read `role`; `amr` lets sensitive endpoints demand that 2FA was
genuinely performed rather than trusting a flag on the user record.

Validation is strict: issuer, audience, lifetime and signing key are all checked, and
`ClockSkew` is cut to **30 seconds** (the framework default of 5 minutes means a "15-minute"
token really lives 20).

### Refresh-token rotation with reuse detection

Every refresh **consumes** the presented token and issues a brand-new one — tokens are
single-use.

```
POST /api/auth/refresh  { refreshToken }
  ├─ hash it, look it up
  ├─ not found            → 401
  ├─ expired / revoked    → 401
  ├─ ALREADY USED         → the token was replayed: revoke the entire family, 401
  └─ valid                → mark used, issue new access + refresh (same family id)
```

Reuse detection is the point. If an attacker steals a refresh token and redeems it, the
legitimate user's next refresh presents an already-used token — the whole family is revoked
and both parties are logged out. Theft becomes a detectable event instead of a silent,
indefinite session. Rotation happens inside a transaction so two concurrent refreshes
cannot both succeed.

#### The 20-second rotation window

That rule is right for a token replayed an hour later and wrong for two things that happen
constantly:

- **Two tabs refresh at the same moment.** One wins; the other arrives with a token the
  first has just spent.
- **A response is lost.** The server rotated, the reply never arrived, and the client
  retries with the only token it still holds.

Neither is theft, and under the bare rule both sign the shop out. So one rotation answers
everyone presenting that token for the next **20 seconds** (`Jwt:RefreshRotationGraceSeconds`,
0 to disable). Concurrent callers share the single in-flight rotation rather than starting
two; a caller arriving shortly after is handed the same tokens the first one got.

The database is untouched by this — it keeps its strict one-use rule, and the window lives
in `AuthCommandService`, keyed by the *hash* of the presented token.

What it costs: an attacker replaying a stolen token inside those 20 seconds receives the
same tokens the victim just did, rather than tripping the alarm. Detection is delayed, not
lost — the moment either party rotates again, the other's copy is a spent token and the
family burns. Twenty seconds is chosen to cover a round trip on a slow connection and
nothing more.

It lives and dies with the process. A restart in the middle of a race drops the window and
the second caller falls through to the database — safe, just a re-login. That is the whole
failure mode: the API is one process serving one shop, and the window is deliberately
smaller than the problem it would take to need anything more.

Measured against the running API, eight refreshes fired at once on one token:

| | grace off | grace on (20s) |
| --- | --- | --- |
| Statuses | `401` × 7, `200` × 1 | `200` × 8 |
| Rotations | 2 | **1** |
| Family afterwards | revoked — everyone signed out | alive |
| Genuine replay after the window | 401 + family revoked | 401 + family revoked |

The last row is the one that matters: the window does not weaken reuse detection, it only
stops it firing on the shop's own traffic.

Refresh tokens are delivered as **`HttpOnly`, `Secure`, `SameSite=Strict`** cookies rather
than JSON, which puts them out of reach of XSS. The access token lives in memory in the SPA
— never `localStorage`.

### Two-factor

Enrolment returns a TOTP secret and `otpauth://` URI once, at setup time, and never again.
Ten single-use recovery codes are generated, shown once, and stored hashed. The 2FA
verification endpoint is rate-limited to 5 attempts per 15 minutes per user, because a
6-digit code is only 1 000 000 possibilities and unlimited guessing walks straight through
it.

### Google OAuth

Authorization-code flow with PKCE and a `state` parameter bound to the session for CSRF
protection. An external login is linked to an existing staff account by **verified** email
only — an unverified Google email cannot claim an account. First-time external logins do
not auto-provision staff: an Owner invites the account first. Otherwise anyone with a
Google account would be able to create themselves a foothold.

## Authorization

Policy-based, checked at the controller:

| Policy | Requirement |
| --- | --- |
| `StaffOnly` | role `Staff` or `Owner` |
| `OwnerOnly` | role `Owner` — settings, staff management, product deletion |
| `MfaVerified` | `amr` contains `mfa` — required for anything money- or settings-related |

Every admin endpoint carries an explicit policy. There is no "authenticated is good enough"
default, and no endpoint relies on the UI hiding a button.

## Input validation

Three layers, deliberately redundant:

1. **Client** — immediate feedback only. Assumed hostile; proves nothing.
2. **API** — FluentValidation on every request DTO: types, lengths, ranges, formats,
   allowed enum values. Runs before any service logic.
3. **Database** — every stored procedure re-validates, plus `CHECK` constraints, foreign
   keys and `NOT NULL` on the columns themselves.

Pakistani phone numbers are normalised and validated against `^(\+92|0)3\d{9}$`. Money is
`DECIMAL(12,2)` end to end — never `float`, which cannot represent Rs 18,500.55 exactly.

## Injection, XSS, CSRF

- **SQL injection** — parameterised stored-procedure calls only. No dynamic SQL is
  concatenated anywhere; the one place dynamic ordering is needed uses a whitelist of
  column names, not the caller's string.
- **XSS** — React escapes by default; there is no `dangerouslySetInnerHTML` in the codebase.
  A restrictive Content-Security-Policy is served, with no `unsafe-inline` for scripts.
- **CSRF** — the API is stateless and reads bearer tokens from the `Authorization` header,
  which is not sent automatically cross-origin. The refresh cookie is `SameSite=Strict`.
- **CORS** — an explicit allow-list of origins with credentials enabled. Never `*`, which
  browsers refuse alongside credentials anyway. See below.

## CORS

The policy is one allow-list, applied by the default policy before authentication so a
rejected preflight never reaches an endpoint:

```csharp
.WithOrigins(auth.AllowedOrigins)   // explicit; empty means same-origin only
.AllowAnyHeader().AllowAnyMethod()
.AllowCredentials()                 // the refresh cookie rides on these requests
.WithExposedHeaders("X-Correlation-Id")
.SetPreflightMaxAge(TimeSpan.FromMinutes(10))
```

Three things worth knowing:

**An Origin is matched as an exact string.** `https://saadsshop.pk/` — with the trailing
slash — matches nothing, and the only symptom is a CORS error in a browser console the
server never sees. Startup validation now rejects any entry that is not bare
`scheme://host[:port]`, so a typo fails at boot with a message naming the rule instead of
becoming someone else's afternoon. `http://localhost` and `http://127.0.0.1` are likewise
different origins; both are listed in development.

**A refused origin still gets a normal response from `curl`.** CORS is enforced by the
browser, not the server: the API answers, omits the `Access-Control-Allow-Origin` header,
and the browser discards the response. Testing with `curl` and seeing `200` is not a hole —
the absence of the header is the control.

**CORS and the refresh cookie have to agree.** The cookie is `SameSite=Strict`, so a
front end on a genuinely different site cannot send it however permissive CORS is. The
supported deployments are same-origin (the SPA served by, or proxied through, the API's
origin — what development does through Vite) or same-site (`saadsshop.pk` calling
`api.saadsshop.pk`, where Strict still applies). Hosting the SPA on an unrelated domain
means relaxing `RefreshCookieSameSiteStrict`, and that is a deliberate decision with its
own CSRF cost, not a configuration detail.

## Transport and headers

HTTPS only, HSTS with a one-year max-age in production, and:

```
Content-Security-Policy: default-src 'self'; img-src 'self' data:; frame-ancestors 'none'
X-Content-Type-Options: nosniff
Referrer-Policy: strict-origin-when-cross-origin
Permissions-Policy: geolocation=(), camera=(), microphone=()
X-Frame-Options: DENY
```

`Server` and `X-Powered-By` are stripped — free reconnaissance otherwise.

## Rate limiting

ASP.NET Core's built-in limiter, partitioned by IP for anonymous traffic and by user id
once authenticated:

| Endpoint group | Limit |
| --- | --- |
| `POST /api/auth/login` | 5 / 15 min per IP + per account |
| `POST /api/auth/2fa/verify` | 5 / 15 min per user |
| `POST /api/orders` | 10 / hour per IP |
| Everything else | 100 / minute |

## Secrets

Nothing secret is committed. Development uses `dotnet user-secrets`; production reads
environment variables. `appsettings.json` holds only non-sensitive defaults, with secret
keys present but empty so the shape is documented. Startup **fails loudly** if the JWT
signing key is missing, shorter than 32 bytes, or still equal to a placeholder — a
misconfigured deploy should not come up quietly signing tokens with a guessable key.

## Where the tokens live, and why

Asked often enough to write down.

| | Where | Lifetime |
| --- | --- | --- |
| Access token | A module variable in `lib/api.ts` — memory only | 15 minutes |
| Refresh token | `HttpOnly; Secure; SameSite=Strict; Path=/api/auth` | 14 days, rotating |

**Neither is in `localStorage`.** The only thing the shop keeps there is the
shopping cart, which holds no secrets. `api.test.ts` asserts the access token
never reaches storage, and `AuthControllerTests` asserts every flag on the
refresh cookie; both run in CI.

### Why the access token is not also in a cookie

It is the obvious next question, and the answer is that it would trade a small
gain for a real cost.

`HttpOnly` defends against **exfiltration** — a script reading the token and
sending it somewhere to be used later, elsewhere. It does not defend against
abuse in the page: script running on the page can call the API as the user
whatever the token is kept in, simply by making requests. So the gain is
bounded, and bounded further by the fifteen-minute lifetime: a token that has
been stolen is useful for the rest of that quarter hour and no longer.

Against that, a cookie is attached by the browser to every request to the
origin. That is what makes CSRF possible, and defending against it means an
anti-forgery token on every mutating endpoint — new code, in the part of the
system where a mistake is most expensive. `SameSite=Strict` covers most of it,
but "most" is doing real work in that sentence.

The credential actually worth protecting is the refresh token: it is long-lived
and it mints access tokens. It **is** `HttpOnly`, it rotates on every use, and
replaying a spent one revokes the whole family. That is where the protection
belongs, and that is where it is.

This is the standard shape for a single-page application, and the shop is not
unusual enough to depart from it. If the trade-off ever changes — a stricter
compliance requirement, say — the work is: read the token from a cookie in the
JWT bearer handler's `OnMessageReceived`, add anti-forgery to every mutating
endpoint, and keep `SameSite=Strict`.

## What is deliberately not here

- **No card payments.** The shop takes cash on delivery, WhatsApp orders and
  reserve-pay-in-shop. No PAN, CVV or cardholder data is collected, stored or transmitted,
  which keeps PCI-DSS out of scope entirely. The Settings screen shows "Card payment — not
  set up yet" and that is the honest state.
- **No customer PII beyond what an order needs** — name, phone, delivery address. No date
  of birth, no CNIC, no marketing profile.
