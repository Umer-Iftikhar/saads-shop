/** Formatting helpers. Money and dates are shown the way the shop says them. */

/**
 * Rupees, as the design writes them: `Rs 18,500`.
 *
 * No decimals — the shop deals in whole rupees and "Rs 18,500.00" reads like a
 * bank statement, not a bazaar. The API still sends decimals, because a total
 * that cannot represent paisa is a total that will eventually be wrong.
 */
export function formatPkr(amount: number | null | undefined): string {
  if (amount == null || Number.isNaN(amount)) return 'Rs 0';

  const rounded = Math.round(amount);
  return `Rs ${rounded.toLocaleString('en-US')}`;
}

/** `4 Sep` — the short form the shop panel uses in lists. */
export function formatShortDate(iso: string | Date | null | undefined): string {
  if (!iso) return '—';
  const date = typeof iso === 'string' ? new Date(iso) : iso;
  if (Number.isNaN(date.getTime())) return '—';

  return date.toLocaleDateString('en-GB', { day: 'numeric', month: 'short' });
}

/** `Thursday, 4 September` — the overview's header line. */
export function formatLongDate(iso: string | Date | null | undefined): string {
  if (!iso) return '';
  const date = typeof iso === 'string' ? new Date(iso) : iso;
  if (Number.isNaN(date.getTime())) return '';

  return date.toLocaleDateString('en-GB', { weekday: 'long', day: 'numeric', month: 'long' });
}

/** `0301 234 5678` — grouped for reading aloud down a phone line. */
export function formatPhone(phone: string | null | undefined): string {
  if (!phone) return '';
  const digits = phone.replace(/\D/g, '');
  if (digits.length !== 11) return phone;

  return `${digits.slice(0, 4)} ${digits.slice(4, 7)} ${digits.slice(7)}`;
}

/** An ISO date string for `<input type="date">`, which will not take a Date. */
export function toDateInput(date: Date): string {
  return date.toISOString().slice(0, 10);
}

/**
 * A wa.me link that opens WhatsApp with the message pre-filled.
 * The number must be international and digits-only, so the local `03…` form is
 * converted rather than passed through.
 */
export function whatsAppLink(localNumber: string, message?: string): string {
  const digits = localNumber.replace(/\D/g, '');
  const international = digits.startsWith('0') ? `92${digits.slice(1)}` : digits;
  const text = message ? `?text=${encodeURIComponent(message)}` : '';

  return `https://wa.me/${international}${text}`;
}
