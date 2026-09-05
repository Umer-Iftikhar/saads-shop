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
      <main id="main" className="page py-10">
        <h1 className="display-page mb-6 mt-0">Your cart</h1>
        <EmptyState
          title="Nothing in the cart yet"
          detail="Pick a bridal set, or build one from scratch with the cloth you like."
          action={
            <div className="flex flex-wrap justify-center gap-2.5">
              <Link to="/wedding-sets" className="btn btn-primary">See wedding sets</Link>
              <Link to="/build-your-set" className="btn btn-secondary">Build your set</Link>
            </div>
          }
        />
      </main>
    );
  }

  return (
    <main id="main" className="page pb-16 pt-9">
      <h1 className="display-page mb-6 mt-0">Your cart</h1>

      <div className="grid grid-cols-1 items-start gap-8 2xl:grid-cols-[1fr_360px]">
        {/* ── lines ────────────────────────────────────────────────── */}
        <div className="flex flex-col gap-3.5">
          {cart.items.map(item => {
            const key = cartKey(item);

            // A row on a desktop and a wrapped stack on a phone, where five
            // columns would each be too narrow to touch.
            return (
              <div key={key} className="card flex-row flex-wrap items-center gap-3 p-4 sm:gap-[18px]">
                <div
                  className="washed h-[88px] w-[88px] flex-none rounded-[20px]"
                  aria-hidden="true"
                  style={{ background: fabricBackground(item.swatchColorValue, item.swatchWeave) }}
                />

                <div className="min-w-40 flex-1">
                  <Link to={`/product/${item.slug}`} className="font-heading text-xl text-text">
                    {item.name}
                  </Link>
                  <div className="mt-[3px] text-[13px] text-neutral-600">
                    {[item.bedSize, item.swatchName, item.pieces].filter(Boolean).join(' · ')}
                  </div>
                </div>

                <div className="flex items-center gap-2.5">
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
                  <span className="w-6 text-center font-bold" aria-live="polite">
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

                <div className="w-[104px] text-right font-bold">
                  {formatPkr(item.price * item.quantity)}
                </div>

                <button
                  type="button"
                  className="btn btn-ghost ml-auto sm:ml-0"
                  onClick={() => cart.remove(key)}
                  aria-label={`Remove ${item.name} from the cart`}
                >
                  Remove
                </button>
              </div>
            );
          })}

          {cart.items.some(i => i.bedSize) && (
            <div className="flex items-center gap-3.5 rounded-lg bg-accent-2-100 px-5 py-5 text-sm text-accent-2-800">
              Measurements needed for the stitching — we will call before we cut.
            </div>
          )}
        </div>

        {/* ── checkout ─────────────────────────────────────────────── */}
        <form className="card gap-0 p-6 shadow-md" onSubmit={submit} noValidate>
          <h2 className="mb-3.5 text-[23px]">Checkout</h2>

          <div className="flex justify-between py-[7px] text-sm">
            <span>Items</span><span>{formatPkr(cart.subtotal)}</span>
          </div>
          <div className="flex justify-between border-b border-divider py-[7px] text-sm">
            <span>Delivery in {settings.data?.city ?? 'Rawalpindi'}</span>
            <span>{deliveryCharge === 0 ? 'Free' : formatPkr(deliveryCharge)}</span>
          </div>
          <div className="flex items-baseline justify-between pb-1 pt-3.5">
            <span className="text-[13px] text-neutral-600">To pay</span>
            <span className="font-heading text-[28px]">
              {formatPkr(cart.subtotal + deliveryCharge)}
            </span>
          </div>
          <p className="mb-4 mt-0 text-[11px] text-neutral-600">
            The shop confirms the final total when it takes the order.
          </p>

          <fieldset className="mb-4 border-0 p-0">
            <legend className="mb-2.5 p-0 text-xs text-neutral-600">
              HOW WOULD YOU LIKE TO PAY?
            </legend>

            <div className="flex flex-col gap-2.5">
              {available.map(option => (
                <label key={option} className="flex items-center gap-2.5 text-sm">
                  <input
                    type="radio"
                    name="paymentMethod"
                    value={option}
                    checked={method === option}
                    onChange={() => setMethod(option)}
                    className="h-4 w-4 accent-accent"
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
            <div role="alert" className="my-2.5 rounded-md bg-accent-100 px-4 py-3 text-sm text-accent-800">
              {formError}
            </div>
          )}

          <button
            type="submit"
            className="btn btn-primary btn-block mt-1.5 p-3"
            disabled={placeOrder.isPending}
          >
            {placeOrder.isPending ? 'Placing your order…' : 'Place order'}
          </button>
        </form>
      </div>
    </main>
  );
}
