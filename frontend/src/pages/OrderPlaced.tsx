import { Link, useLocation, Navigate } from 'react-router-dom';
import { formatPkr } from '../lib/format';
import { useShopSettings } from '../hooks/queries';
import type { OrderConfirmation } from '../types/api';

const PAYMENT_LINE: Record<string, string> = {
  CashOnDelivery: 'Paying cash on delivery · rider between 5pm and 8pm',
  WhatsApp:       'We will message you on WhatsApp to confirm',
  ReserveInShop:  'Reserved · come and see the cloth in daylight at the shop',
  Card:           'Card payment',
};

/**
 * Frame 06 — order placed.
 *
 * The confirmation arrives through router state from the checkout. Landing here
 * directly means there is nothing to show, so it redirects rather than
 * rendering an empty receipt — and the state is not persisted, because an order
 * receipt should not reappear days later on a shared phone.
 */
export function OrderPlaced() {
  const location = useLocation();
  const settings = useShopSettings();
  const confirmation = (location.state as { confirmation?: OrderConfirmation } | null)?.confirmation;

  if (!confirmation) return <Navigate to="/" replace />;

  return (
    <main id="main" className="page pb-16 pt-[86px] text-center">
      <div aria-hidden="true" className="mx-auto mb-6 h-[92px] w-[92px] rounded-full bg-accent-2-500" />

      <h1 className="display-thanks mb-3 mt-0">Shukriya!</h1>

      <p className="mb-1.5 mt-0 text-[19px] text-neutral-700">
        Order <strong>{confirmation.reference}</strong> is with the shop.
        {confirmation.lines.some(l => l.bedSize) && ' We will call to confirm your measurements.'}
      </p>

      <p className="mb-7 mt-0 text-[15px] text-neutral-600">
        {PAYMENT_LINE[confirmation.paymentMethod] ?? 'We will be in touch to confirm.'}
      </p>

      {/* A quiet receipt — enough to check against, not a full invoice. */}
      <div className="card mx-auto mb-7 max-w-[460px] p-6 text-left">
        {confirmation.lines.map(line => (
          <div key={line.orderLineId} className="flex justify-between gap-3 border-b border-divider py-2 text-sm">
            <span>
              {line.productName}
              {line.quantity > 1 && ` ×${line.quantity}`}
              {(line.swatchName || line.bedSize) && (
                <span className="block text-xs text-neutral-600">
                  {[line.bedSize, line.swatchName].filter(Boolean).join(' · ')}
                </span>
              )}
            </span>
            <span className="whitespace-nowrap font-bold">{formatPkr(line.lineTotal)}</span>
          </div>
        ))}

        <div className="flex justify-between pt-2.5 text-sm">
          <span>Delivery</span>
          <span>{confirmation.deliveryCharge === 0 ? 'Free' : formatPkr(confirmation.deliveryCharge)}</span>
        </div>

        <div className="flex items-baseline justify-between pt-2.5">
          <span className="text-[13px] text-neutral-600">Total</span>
          <span className="font-heading text-[26px]">{formatPkr(confirmation.total)}</span>
        </div>
      </div>

      <p className="mb-6 text-[13px] text-neutral-600">
        Keep the order number. You can check on it any time with that and your phone number
        {settings.data?.whatsAppNumber ? `, or message the shop on ${settings.data.whatsAppNumber}` : ''}.
      </p>

      <Link to="/" className="btn btn-secondary btn-lg">Back to the shop</Link>
    </main>
  );
}
