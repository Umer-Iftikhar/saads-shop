/**
 * Client-side validation.
 *
 * This layer exists for feedback, not for safety — it tells someone their phone
 * number looks wrong before they wait for a round trip. The API validates every
 * one of these rules again, and the stored procedures validate them a third
 * time. Nothing here is trusted by anything downstream.
 *
 * The rules mirror the server deliberately: a client that accepts what the
 * server rejects produces a confusing error, and one that rejects what the
 * server accepts silently loses the customer an order.
 */

export interface FieldErrors {
  [field: string]: string;
}

// ── phone ─────────────────────────────────────────────────────────────────

/** Matches `PhoneNumber.Normalise` on the server: +92 / 92 / 0 / bare, spaced or dashed. */
const PHONE_PATTERN = /^(\+92|92|0)?[\s-]?3\d{2}[\s-]?\d{3}[\s-]?\d{4}$/;

export function isValidPhone(input: string): boolean {
  return PHONE_PATTERN.test(input.trim());
}

/** The local `03xxxxxxxxx` form, or null when it is not a Pakistani mobile number. */
export function normalisePhone(input: string): string | null {
  let digits = input.replace(/[\s\-().]/g, '');

  if (digits.startsWith('+92'))       digits = `0${digits.slice(3)}`;
  else if (digits.startsWith('0092')) digits = `0${digits.slice(4)}`;
  else if (digits.startsWith('92') && digits.length === 12) digits = `0${digits.slice(2)}`;
  else if (digits.startsWith('3') && digits.length === 10)  digits = `0${digits}`;

  return /^03\d{9}$/.test(digits) ? digits : null;
}

// ── date ranges ───────────────────────────────────────────────────────────

/** Parses an `<input type="date">` value at local midnight, or null. */
function parseDateInput(value: string): Date | null {
  if (!value) return null;
  const date = new Date(`${value}T00:00:00`);
  return Number.isNaN(date.getTime()) ? null : date;
}

export interface DateRangeRules {
  /** Widest span allowed, inclusive of both ends. */
  maxSpanDays?: number;
  /** Whether the range may extend past today. */
  allowFuture?: boolean;
  /** Whether both ends may be blank, meaning "no date filter". */
  allowOpenRange?: boolean;
  earliestYear?: number;
}

/**
 * The browser-side twin of the server's `[DateRange]` attribute.
 *
 * Returns a map keyed by field name so a form can put each message under the
 * input it belongs to, exactly as the API's `errors` object does — which means
 * one rendering path handles both local and server validation.
 */
export function validateDateRange(
  from: string,
  to: string,
  rules: DateRangeRules = {},
): FieldErrors {
  const {
    maxSpanDays = 366,
    allowFuture = false,
    allowOpenRange = true,
    earliestYear = 2000,
  } = rules;

  const errors: FieldErrors = {};

  if (!from && !to) {
    if (!allowOpenRange) errors.fromDate = 'Choose a date range.';
    return errors;
  }

  const fromDate = parseDateInput(from);
  const toDate   = parseDateInput(to);

  if (from && !fromDate) errors.fromDate = 'That is not a date we can read.';
  if (to && !toDate)     errors.toDate   = 'That is not a date we can read.';
  if (errors.fromDate || errors.toDate) return errors;

  const today = new Date();
  today.setHours(0, 0, 0, 0);
  const earliest = new Date(earliestYear, 0, 1);

  if (fromDate) {
    if (fromDate < earliest) errors.fromDate = `The start date must be on or after 1 Jan ${earliestYear}.`;
    else if (!allowFuture && fromDate > today) errors.fromDate = 'The start date cannot be in the future.';
  }

  if (toDate) {
    if (toDate < earliest) errors.toDate = `The end date must be on or after 1 Jan ${earliestYear}.`;
    else if (!allowFuture && toDate > today) errors.toDate = 'The end date cannot be in the future.';
  }

  if (fromDate && toDate && !errors.fromDate && !errors.toDate) {
    if (fromDate > toDate) {
      errors.fromDate = 'The start date must be on or before the end date.';
    } else {
      // +1 because the range includes both ends: 1 Jan to 1 Jan is one day.
      const days = Math.round((toDate.getTime() - fromDate.getTime()) / 86_400_000) + 1;

      if (days > maxSpanDays) {
        errors.toDate =
          `That range covers ${days.toLocaleString()} days. Please choose ${maxSpanDays.toLocaleString()} days or fewer.`;
      }
    }
  }

  return errors;
}

/** A single date that must not be in the future — "delivered on", "measured on". */
export function validateNotFuture(value: string, label = 'That date'): string | null {
  if (!value) return null;

  const date = new Date(`${value}T00:00:00`);
  if (Number.isNaN(date.getTime())) return 'That is not a date we can read.';

  const today = new Date();
  today.setHours(23, 59, 59, 999);

  return date > today ? `${label} cannot be in the future.` : null;
}

// ── checkout ──────────────────────────────────────────────────────────────

export interface CheckoutFields {
  customerName: string;
  phone: string;
  deliveryAddress: string;
}

/**
 * Validates the checkout form. Mirrors `PlaceOrderRequest`'s attributes, so a
 * form that passes here should not come back with a 400.
 */
export function validateCheckout(fields: CheckoutFields): FieldErrors {
  const errors: FieldErrors = {};
  const name = fields.customerName.trim();
  const address = fields.deliveryAddress.trim();

  if (!name) errors.customerName = 'Please tell us your name.';
  else if (name.length < 2) errors.customerName = 'That name looks too short.';
  else if (name.length > 128) errors.customerName = 'That name is too long.';

  if (!fields.phone.trim()) errors.phone = 'A phone number is required.';
  else if (!isValidPhone(fields.phone)) {
    errors.phone = 'That phone number does not look right. Use the form 03xx xxx xxxx.';
  }

  if (!address) errors.deliveryAddress = 'Please give an address in Rawalpindi.';
  else if (address.length < 5) errors.deliveryAddress = 'That address looks too short.';
  else if (address.length > 400) errors.deliveryAddress = 'That address is too long.';

  return errors;
}

export function hasErrors(errors: FieldErrors): boolean {
  return Object.keys(errors).length > 0;
}

/**
 * Flattens the API's `errors` object into the same shape the local validators
 * produce, so a form renders server-side failures through one path.
 * Keys are lower-cased at the first letter because the API sends PascalCase.
 */
export function fromApiErrors(apiErrors: Record<string, string[]>): FieldErrors {
  const errors: FieldErrors = {};

  for (const [key, messages] of Object.entries(apiErrors)) {
    if (!messages?.length) continue;
    const field = key.charAt(0).toLowerCase() + key.slice(1);
    errors[field] = messages[0];
  }

  return errors;
}
