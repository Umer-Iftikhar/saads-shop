/**
 * TypeScript mirrors of the API's response and request shapes.
 *
 * Hand-written rather than generated, because the set is small and stable and a
 * generator would be one more thing to keep running. They are checked against
 * the real API by the type errors that appear the moment a field is renamed on
 * either side and the two are used together.
 */

// ── shared ────────────────────────────────────────────────────────────────

export interface PagedResponse<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

/**
 * RFC 7807 problem details, as every failure comes back.
 *
 * `responseCode` is the stored procedure's own code — which is an HTTP status,
 * so it equals `status`. `errors` is present only on validation failures.
 */
export interface ProblemDetails {
  type?: string;
  title: string;
  status: number;
  detail?: string;
  instance?: string;
  responseCode?: number;
  correlationId?: string;
  errors?: Record<string, string[]>;
}

// ── catalogue ─────────────────────────────────────────────────────────────

export interface Category {
  categoryId: number;
  name: string;
  slug: string;
}

export interface Swatch {
  swatchId: number;
  name: string;
  /** Hex, or `oklch(...)` for Gold and Plum. Passed to CSS untouched. */
  colorValue: string;
  weave: string;
  imagePath?: string | null;
}

export interface BedSize {
  bedSizeCode: 'Single' | 'Double' | 'King';
  name: string;
  priceAdjustment: number;
}

export interface ProductSummary {
  productId: number;
  name: string;
  slug: string;
  categoryName: string;
  kicker?: string | null;
  blurb?: string | null;
  price: number;
  pieces?: string | null;
  /** Whether it can be bought today. The count is the shop's business. */
  inStock: boolean;
  defaultSwatchId?: number | null;
  swatchColorValue?: string | null;
  swatchWeave?: string | null;
}

export interface ProductDetail {
  productId: number;
  name: string;
  slug: string;
  categoryName: string;
  categorySlug: string;
  kicker?: string | null;
  blurb?: string | null;
  longDescription?: string | null;
  price: number;
  pieces?: string | null;
  stitchingDays: number;
  inStock: boolean;
  defaultSwatchId?: number | null;
  swatches: Swatch[];
  related: ProductSummary[];
}

// ── shop ──────────────────────────────────────────────────────────────────

export type PaymentMethod = 'CashOnDelivery' | 'WhatsApp' | 'ReserveInShop' | 'Card';

export interface ShopSettingsPublic {
  shopName: string;
  city: string;
  addressLine: string;
  whatsAppNumber: string;
  bannerText?: string | null;
  openingHours?: string | null;
  deliveryCharge: number;
  freeDeliveryThreshold: number;
  /** Only the methods actually switched on, so a stale page cannot offer a dead one. */
  paymentMethods: PaymentMethod[];
}

// ── orders ────────────────────────────────────────────────────────────────

/** What a cart line sends. Note the absence of anything money-shaped. */
export interface CartLineRequest {
  productId: number;
  quantity: number;
  swatchId?: number | null;
  bedSize?: string | null;
}

export interface PlaceOrderRequest {
  customerName: string;
  phone: string;
  deliveryAddress: string;
  area?: string | null;
  paymentMethod: PaymentMethod;
  notes?: string | null;
  lines: CartLineRequest[];
}

export interface OrderLine {
  orderLineId: number;
  productId: number;
  productName: string;
  swatchName?: string | null;
  swatchColorValue?: string | null;
  swatchWeave?: string | null;
  bedSize?: string | null;
  unitPrice: number;
  quantity: number;
  lineTotal: number;
}

export interface OrderConfirmation {
  reference: string;
  status: string;
  paymentMethod: PaymentMethod;
  subtotal: number;
  deliveryCharge: number;
  total: number;
  deliveryAddress: string;
  customerName: string;
  placedAt: string;
  lines: OrderLine[];
}

export interface OrderTracking {
  reference: string;
  status: string;
  paymentMethod: PaymentMethod;
  subtotal: number;
  deliveryCharge: number;
  total: number;
  customerName: string;
  placedAt: string;
  lines: OrderLine[];
}

// ── set builder ───────────────────────────────────────────────────────────

export interface SetBuilderQuoteRequest {
  sheetProductId: number;
  curtainProductId: number;
  cushionProductId: number;
  bedSize: string;
}

export interface SetBuilderLine {
  /** Bistar, Parde or Cushions. */
  slot: string;
  productId: number;
  productName: string;
  unitPrice: number;
  inStock: boolean;
}

export interface SetBuilderQuote {
  bedSize: string;
  total: number;
  lines: SetBuilderLine[];
}
