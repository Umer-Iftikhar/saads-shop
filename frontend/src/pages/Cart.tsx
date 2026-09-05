import { useState } from 'react';
import { useMutation } from '@tanstack/react-query';
import { Link, useNavigate } from 'react-router-dom';
import { Field } from '../components/Field';
import { EmptyState } from '../components/Feedback';
import { api, ApiError } from '../lib/api';
import { fabricBackground } from '../lib/fabric';
import { formatPkr } from '../lib/format';
import { useShopSettings } from '../hooks/queries';
import { useCart } from '../state/cart';
import { cartKey } from '../state/cartKey';
import { fromApiErrors, hasErrors, validateCheckout } from '../lib/validation';
import type { FieldErrors } from '../lib/validation';
import type { OrderConfirmation, PaymentMethod, PlaceOrderRequest } from '../types/api';

const METHOD_LABELS: Record<PaymentMethod, string> = {
  CashOnDelivery: 'Cash on delivery',
  WhatsApp:       'Order on WhatsApp',
  ReserveInShop:  'Reserve, pay in the shop',
  Card:           'Card',
};

/**
 * Frame 05 — cart and checkout.
 *
 * The totals shown here are the client's arithmetic over catalogue prices, and
 * they are labelled as an estimate for exactly that reason: the binding figure
 * is the one the checkout procedure computes under lock. When the two differ —
 * a price changed while the cart sat open — the server's answer wins and the
 * confirmation shows it.
 */
export function Cart() {
  const cart = useCart();
  const navigate = useNavigate();
  const settings = useShopSettings();

  const [fields, setFields] = useState({ customerName: '', phone: '', deliveryAddress: '', area: '', notes: '' });
  const [method, setMethod] = useState<PaymentMethod>('CashOnDelivery');
  const [errors, setErrors] = useState<FieldErrors>({});
  const [formError, setFormError] = useState<string | null>(null);

  const placeOrder = useMutation({
    mutationFn: (request: PlaceOrderRequest) => api.post<OrderConfirmation>('/orders', request),
    onSuccess: confirmation => {
      cart.clear();
      // Handed through router state rather than refetched: the confirmation is
      // the only time this data is available without the customer's phone.
      navigate('/order-placed', { state: { confirmation }, replace: true });
    },
    onError: (error: ApiError) => {
      if (error.isValidation && Object.keys(error.fieldErrors).length) {
        setErrors(fromApiErrors(error.fieldErrors));
        setFormError(null);
      } else {
        // 409 — an item sold out between browsing and checkout. The message
        // names the item, so it is shown as-is.
        setFormError(error.message);
      }
    },
  });

  const available = settings.data?.paymentMethods ?? ['CashOnDelivery'];
  const deliveryCharge = settings.data
    ? (cart.subtotal >= settings.data.freeDeliveryThreshold ? 0 : settings.data.deliveryCharge)
    : 0;

  function submit(event: React.FormEvent) {
    event.preventDefault();
    setFormError(null);

    const found = validateCheckout(fields);
    setErrors(found);
    if (hasErrors(found)) {
      // Move focus to the first problem so a keyboard user is not left
      // guessing which field the error banner refers to.
      const first = Object.keys(found)[0];
      document.querySelector<HTMLElement>(`[data-field="${first}"]`)?.focus();
      return;
    }

    placeOrder.mutate({
      customerName: fields.customerName.trim(),
      phone: fields.phone.trim(),
      deliveryAddress: fields.deliveryAddress.trim(),
      area: fields.area.trim() || null,
      paymentMethod: method,
      notes: fields.notes.trim() || null,
      lines: cart.toRequestLines(),
    });
  }

  if (cart.items.length === 0) {
    return (
      <main id="main" className="page page-pad" style={{ paddingBlock: 40 }}>
        <h1 className="display-page" style={{ margin: '0 0 26px' }}>Your cart</h1>
        <EmptyState
          title="Nothing in the cart yet"
          detail="Pick a bridal set, or build one from scratch with the cloth you like."
          action={
            <div style={{ display: 'flex', gap: 10, justifyContent: 'center', flexWrap: 'wrap' }}>
              <Link to="/wedding-sets" className="btn btn-primary">See wedding sets</Link>
              <Link to="/build-your-set" className="btn btn-secondary">Build your set</Link>
            </div>
          }
        />
      </main>
    );
  }

  return (
    <main id="main" className="page page-pad" style={{ paddingBlock: '36px 64px' }}>
      <h1 className="display-page" style={{ margin: '0 0 26px' }}>Your cart</h1>

      <div className="with-rail">
        {/* ── lines ────────────────────────────────────────────────── */}
        <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
          {cart.items.map(item => {
            const key = cartKey(item);

            return (
              <div key={key} className="card cart-line">
                <div
                  className="washed"
                  aria-hidden="true"
                  style={{
                    flex: 'none', width: 88, height: 88, borderRadius: 20,
                    background: fabricBackground(item.swatchColorValue, item.swatchWeave),
                  }}
                />

                <div style={{ flex: 1, minWidth: 160 }}>
                  <Link to={`/product/${item.slug}`} style={{ fontFamily: 'var(--font-heading)', fontSize: 20, color: 'inherit' }}>
                    {item.name}
                  </Link>
                  <div style={{ fontSize: 13, color: 'var(--color-neutral-600)', marginTop: 3 }}>
                    {[item.bedSize, item.swatchName, item.pieces].filter(Boolean).join(' · ')}
                  </div>
                </div>

                <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                  <button
                    type="button"
                    className="btn btn-secondary btn-icon"
                    onClick={() => cart.setQuantity(key, item.quantity - 1)}
                    aria-label={`Reduce ${item.name} to ${item.quantity - 1}`}
                  >
                    −
                  </button>

                  {/*  The quantity is a live region so a screen-reader user
                      hears it change without re-reading the whole row.      */}
                  <span style={{ width: 24, textAlign: 'center', fontWeight: 700 }} aria-live="polite">
                    {item.quantity}
                  </span>

                  <button
                    type="button"
                    className="btn btn-secondary btn-icon"
                    onClick={() => cart.setQuantity(key, item.quantity + 1)}
                    aria-label={`Increase ${item.name} to ${item.quantity + 1}`}
                  >
                    +
                  </button>
                </div>

                <div style={{ width: 104, textAlign: 'right', fontWeight: 700 }}>
                  {formatPkr(item.price * item.quantity)}
                </div>

                <button
                  type="button"
                  className="btn btn-ghost"
                  onClick={() => cart.remove(key)}
                  aria-label={`Remove ${item.name} from the cart`}
                >
                  Remove
                </button>
              </div>
            );
          })}

          {cart.items.some(i => i.bedSize) && (
            <div style={{
              display: 'flex', alignItems: 'center', gap: 14, padding: '20px 22px',
              borderRadius: 'var(--radius-lg)', background: 'var(--color-accent-2-100)',
              color: 'var(--color-accent-2-800)', fontSize: 14,
            }}>
              Measurements needed for the stitching — we will call before we cut.
            </div>
          )}
        </div>

        {/* ── checkout ─────────────────────────────────────────────── */}
        <form className="card elev-md" style={{ padding: 24, gap: 0 }} onSubmit={submit} noValidate>
          <h2 style={{ fontSize: 23, marginBottom: 14 }}>Checkout</h2>

          <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 14, padding: '7px 0' }}>
            <span>Items</span><span>{formatPkr(cart.subtotal)}</span>
          </div>
          <div style={{
            display: 'flex', justifyContent: 'space-between', fontSize: 14,
            padding: '7px 0', borderBottom: '1px solid var(--color-divider)',
          }}>
            <span>Delivery in {settings.data?.city ?? 'Rawalpindi'}</span>
            <span>{deliveryCharge === 0 ? 'Free' : formatPkr(deliveryCharge)}</span>
          </div>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', padding: '14px 0 4px' }}>
            <span style={{ fontSize: 13, color: 'var(--color-neutral-600)' }}>To pay</span>
            <span style={{ fontFamily: 'var(--font-heading)', fontSize: 28 }}>
              {formatPkr(cart.subtotal + deliveryCharge)}
            </span>
          </div>
          <p style={{ fontSize: 11, color: 'var(--color-neutral-600)', margin: '0 0 18px' }}>
            The shop confirms the final total when it takes the order.
          </p>

          <fieldset style={{ border: 0, padding: 0, margin: '0 0 18px' }}>
            <legend style={{ fontSize: 12, color: 'var(--color-neutral-600)', marginBottom: 10, padding: 0 }}>
              HOW WOULD YOU LIKE TO PAY?
            </legend>

            <div style={{ display: 'flex', flexDirection: 'column', gap: 11 }}>
              {available.map(option => (
                <label key={option} className="radio" style={{ display: 'flex', alignItems: 'center', gap: 10, fontSize: 14 }}>
                  <input
                    type="radio"
                    name="paymentMethod"
                    value={option}
                    checked={method === option}
                    onChange={() => setMethod(option)}
                    style={{ accentColor: 'var(--color-accent)', width: 16, height: 16 }}
                  />
                  {METHOD_LABELS[option]}
                </label>
              ))}
            </div>
          </fieldset>

          <Field label="Name" error={errors.customerName}>
            {props => (
              <input
                {...props}
                data-field="customerName"
                className="input"
                placeholder="Your name"
                autoComplete="name"
                value={fields.customerName}
                onChange={e => setFields({ ...fields, customerName: e.target.value })}
              />
            )}
          </Field>

          <Field label="Phone / WhatsApp" error={errors.phone} hint="03xx xxx xxxx">
            {props => (
              <input
                {...props}
                data-field="phone"
                className="input"
                type="tel"
                inputMode="tel"
                placeholder="03xx xxx xxxx"
                autoComplete="tel"
                value={fields.phone}
                onChange={e => setFields({ ...fields, phone: e.target.value })}
              />
            )}
          </Field>

          <Field label={`Address in ${settings.data?.city ?? 'Rawalpindi'}`} error={errors.deliveryAddress}>
            {props => (
              <textarea
                {...props}
                data-field="deliveryAddress"
                className="input"
                placeholder="House, street, area"
                autoComplete="street-address"
                value={fields.deliveryAddress}
                onChange={e => setFields({ ...fields, deliveryAddress: e.target.value })}
              />
            )}
          </Field>

          <Field label="Anything we should know? (optional)" error={errors.notes}>
            {props => (
              <input
                {...props}
                data-field="notes"
                className="input"
                placeholder="Call before 6pm"
                value={fields.notes}
                onChange={e => setFields({ ...fields, notes: e.target.value })}
              />
            )}
          </Field>

          {formError && (
            <div role="alert" style={{
              margin: '10px 0', padding: '12px 16px', borderRadius: 'var(--radius-md)',
              background: 'var(--color-accent-100)', color: 'var(--color-accent-800)', fontSize: 14,
            }}>
              {formError}
            </div>
          )}

          <button
            type="submit"
            className="btn btn-primary btn-block"
            style={{ padding: 13, marginTop: 6 }}
            disabled={placeOrder.isPending}
          >
            {placeOrder.isPending ? 'Placing your order…' : 'Place order'}
          </button>
        </form>
      </div>
    </main>
  );
}
