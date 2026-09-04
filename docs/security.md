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
- **CORS** — an explicit allow-list of origins with credentials enabled. Never `*`.

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

## What is deliberately not here

- **No card payments.** The shop takes cash on delivery, WhatsApp orders and
  reserve-pay-in-shop. No PAN, CVV or cardholder data is collected, stored or transmitted,
  which keeps PCI-DSS out of scope entirely. The Settings screen shows "Card payment — not
  set up yet" and that is the honest state.
- **No customer PII beyond what an order needs** — name, phone, delivery address. No date
  of birth, no CNIC, no marketing profile.
