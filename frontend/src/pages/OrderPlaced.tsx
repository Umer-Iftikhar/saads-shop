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
    <main id="main" className="page page-pad" style={{ paddingBlock: '86px 64px', textAlign: 'center' }}>
      <div
        aria-hidden="true"
        style={{
          width: 92, height: 92, borderRadius: '50%',
          background: 'var(--color-accent-2-500)', margin: '0 auto 24px',
        }}
      />

      <h1 className="display-thanks" style={{ margin: '0 0 12px' }}>Shukriya!</h1>

      <p style={{ fontSize: 19, color: 'var(--color-neutral-700)', margin: '0 0 6px' }}>
        Order <strong>{confirmation.reference}</strong> is with the shop.
        {confirmation.lines.some(l => l.bedSize) && ' We will call to confirm your measurements.'}
      </p>

      <p style={{ fontSize: 15, color: 'var(--color-neutral-600)', margin: '0 0 28px' }}>
        {PAYMENT_LINE[confirmation.paymentMethod] ?? 'We will be in touch to confirm.'}
      </p>

      {/* A quiet receipt — enough to check against, not a full invoice. */}
      <div
        className="card"
        style={{ maxWidth: 460, margin: '0 auto 28px', padding: 24, textAlign: 'left' }}
      >
        {confirmation.lines.map(line => (
          <div
            key={line.orderLineId}
            style={{
              display: 'flex', justifyContent: 'space-between', gap: 12, fontSize: 14,
              padding: '8px 0', borderBottom: '1px solid var(--color-divider)',
            }}
          >
            <span>
              {line.productName}
              {line.quantity > 1 && ` ×${line.quantity}`}
              {(line.swatchName || line.bedSize) && (
                <span style={{ display: 'block', fontSize: 12, color: 'var(--color-neutral-600)' }}>
                  {[line.bedSize, line.swatchName].filter(Boolean).join(' · ')}
                </span>
              )}
            </span>
            <span style={{ fontWeight: 700, whiteSpace: 'nowrap' }}>{formatPkr(line.lineTotal)}</span>
          </div>
        ))}

        <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 14, padding: '10px 0 0' }}>
          <span>Delivery</span>
          <span>{confirmation.deliveryCharge === 0 ? 'Free' : formatPkr(confirmation.deliveryCharge)}</span>
        </div>

        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', paddingTop: 10 }}>
          <span style={{ fontSize: 13, color: 'var(--color-neutral-600)' }}>Total</span>
          <span style={{ fontFamily: 'var(--font-heading)', fontSize: 26 }}>{formatPkr(confirmation.total)}</span>
        </div>
      </div>

      <p style={{ fontSize: 13, color: 'var(--color-neutral-600)', marginBottom: 24 }}>
        Keep the order number. You can check on it any time with that and your phone number
        {settings.data?.whatsAppNumber ? `, or message the shop on ${settings.data.whatsAppNumber}` : ''}.
      </p>

      <Link to="/" className="btn btn-secondary btn-lg">Back to the shop</Link>
    </main>
  );
}
