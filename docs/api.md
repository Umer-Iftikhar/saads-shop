# API reference

Base URL `\{host\}/api`. JSON in, JSON out, UTF-8. All times are UTC ISO-8601. All money is
a decimal number of rupees (`18500.00`) — formatting to `Rs 18,500` is the client's job.

Swagger UI is served at `/swagger` in development only.

## Response envelope

Success returns the payload directly with the appropriate status code. Failure returns RFC
7807 problem details, plus the originating database response code when there was one:

```json
{
  "type":   "https://saadsshop.pk/errors/insufficient-stock",
  "title":  "One of the items just went out of stock.",
  "status": 409,
  "responseCode": 3001,
  "correlationId": "0HN7GK2M9V1PL",
  "errors": { "lines[0].quantity": ["Only 1 left in stock."] }
}
```

`errors` is present only for validation failures. `correlationId` matches the Serilog entry,
so a shopkeeper's screenshot is enough to find the log line.

## Authentication

| Method | Route | Auth | Notes |
| --- | --- | --- | --- |
| `POST` | `/auth/login` | — | Email + password. Returns `{ requiresTwoFactor: true, mfaToken }` for staff — never a usable access token on its own. |
| `POST` | `/auth/2fa/verify` | mfaToken | 6-digit TOTP or a recovery code. Returns the access token; sets the refresh cookie. |
| `POST` | `/auth/refresh` | refresh cookie | Rotates. Reuse of a spent token revokes the family. |
| `POST` | `/auth/logout` | Bearer | Revokes the current refresh family and clears the cookie. |
| `GET` | `/auth/google` | — | Starts the OAuth code flow (PKCE + `state`). |
| `GET` | `/auth/google/callback` | — | Completes it, links by verified email, then continues to the 2FA step. |
| `GET` | `/auth/me` | Bearer | Current user, role, whether 2FA is enrolled. |
| `POST` | `/auth/2fa/enroll` | Bearer | Returns the TOTP secret + `otpauth://` URI **once**. |
| `POST` | `/auth/2fa/confirm` | Bearer | Confirms enrolment with a code; returns 10 recovery codes, shown once. |

## Storefront — anonymous

| Method | Route | Notes |
| --- | --- | --- |
| `GET` | `/catalog/products` | `?category=&search=&page=1&pageSize=24`. Cached 10 min. |
| `GET` | `/catalog/products/{id}` | Product + its swatches + related items. |
| `GET` | `/catalog/categories` | Wedding sets, Bed sheets, Curtains, Umbrellas. |
| `GET` | `/catalog/swatches` | The shop's cloth palette. |
| `GET` | `/shop/settings` | Public subset only: banner, address, hours, WhatsApp number, delivery charge, free-delivery threshold, enabled payment methods. |
| `POST` | `/set-builder/quote` | Prices a bistar + parde + cushion combination for a bed size. Server-side pricing — the client never sends a total. |
| `POST` | `/orders` | Places an order. Rate-limited 10/hour/IP. |
| `GET` | `/orders/{reference}` | Confirmation lookup by reference (`SS-2419`) + the phone it was placed with. |

### `POST /orders`

```json
{
  "customer": { "name": "Hina Aslam", "phone": "0301 234 5678",
                "address": "Satellite Town, Block C", "area": "Satellite Town" },
  "paymentMethod": "CashOnDelivery",
  "notes": "Please call before 6pm",
  "lines": [ { "productId": 2, "quantity": 1, "swatchId": 4, "bedSize": "Double" } ]
}
```

Prices, delivery charge and the total are computed server-side from `Products` and
`ShopSettings` **inside the checkout transaction**. Any money field in the request is
ignored. `201 Created` returns the reference, the priced lines and the total.

`409` with `responseCode: 3001` means a line went out of stock between browsing and
checkout — the response names which line, so the cart can show it precisely.

## Shop panel — `StaffOnly` unless noted

| Method | Route | Notes |
| --- | --- | --- |
| `GET` | `/admin/dashboard` | Stat tiles, 12-week sales chart, best sellers, latest orders. Cached 2 min. |
| `GET` | `/admin/orders` | `?status=&search=&page=`. |
| `GET` | `/admin/orders/{id}` | Full order: lines, customer, measurements, history. |
| `PATCH` | `/admin/orders/{id}/status` | Enforces the transition table; `3003` on an illegal move. |
| `POST` | `/admin/orders/{id}/measurements` | Records who took them and when. |
| `GET` | `/admin/inventory` | Stock levels with low-stock flags. |
| `POST` | `/admin/inventory/{productId}/adjust` | Signed delta + reason; every adjustment is audited. |
| `GET`&nbsp;/&nbsp;`POST` | `/admin/products` | List / create. |
| `PUT`&nbsp;/&nbsp;`DELETE` | `/admin/products/{id}` | Update / soft-delete. Delete is `OwnerOnly` and returns `3006` if order lines reference it. |
| `POST`&nbsp;/&nbsp;`DELETE` | `/admin/products/{id}/swatches` | Attach / detach cloth. |
| `GET` | `/admin/stitching-queue` | Jobs grouped Measuring / Cutting / Stitching / Ready. |
| `PATCH` | `/admin/stitching-queue/{jobId}` | Move stage, assign tailor, set due date. |
| `GET` | `/admin/customers` | Repeat buyers, areas, lifetime spend. |
| `GET`&nbsp;/&nbsp;`PUT` | `/admin/settings` | **`OwnerOnly`** + `MfaVerified`. Shop details, payment toggles, delivery charge. |

## Status codes

| Code | When |
| --- | --- |
| `200` / `201` / `204` | Success |
| `400` | Validation failed — `errors` names the fields |
| `401` | Missing, expired or replayed token |
| `403` | Authenticated but not permitted (wrong role, or 2FA not performed) |
| `404` | No such resource |
| `409` | Business-rule conflict — out of stock, illegal status move, duplicate name |
| `429` | Rate limit — `Retry-After` is set |
| `500` | Unexpected. Correlation id returned; details go to the log, not the client. |
