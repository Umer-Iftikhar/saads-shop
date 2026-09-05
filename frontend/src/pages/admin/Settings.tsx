import { useState } from 'react';
import { Field } from '../../components/Field';
import { ErrorState, Loading } from '../../components/Feedback';
import { ApiError } from '../../lib/api';
import { isValidPhone } from '../../lib/validation';
import { useSaveSettings, useSettings } from '../../hooks/adminQueries';
import type { ShopSettings } from '../../types/admin';

/**
 * Frame 14 — Settings.
 *
 * Owner-only, and the API additionally demands that two-factor was actually
 * performed for this session before it will accept a write: this screen decides
 * how the shop takes money.
 */
export function Settings() {
  const settings = useSettings();
  const save = useSaveSettings();

  const [form, setForm] = useState<ShopSettings | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  if (settings.isPending) return <Loading label="Loading the shop's settings…" />;
  if (settings.isError || !settings.data) {
    return <ErrorState title="Could not load settings" onRetry={() => settings.refetch()} />;
  }

  const value = form ?? settings.data;
  const set = <K extends keyof ShopSettings>(key: K, v: ShopSettings[K]) =>
    setForm({ ...value, [key]: v });

  const noPaymentMethod =
    !value.cashOnDeliveryEnabled && !value.whatsAppOrdersEnabled &&
    !value.reserveInShopEnabled && !value.cardPaymentEnabled;

  function submit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    setSaved(false);

    if (!isValidPhone(value.whatsAppNumber)) {
      setError('The WhatsApp number should look like 03xx xxx xxxx.');
      return;
    }
    if (noPaymentMethod) {
      // The procedure refuses this too — a shop with no way to pay cannot take
      // an order — but saying so before the round trip is kinder.
      setError('Leave at least one way for customers to pay.');
      return;
    }

    save.mutate(value, {
      onSuccess: () => { setSaved(true); setForm(null); },
      onError: (e: unknown) => setError(e instanceof ApiError ? e.message : 'Could not save the settings.'),
    });
  }

  return (
    <>
      <header className="mb-5 flex flex-wrap items-center justify-between gap-5">
        <div>
          <h1 className="m-0 text-4xl">Settings</h1>
          <div className="mt-[3px] text-sm text-neutral-600">
            Shop details, payment and delivery
            {settings.data.updatedBy ? ` · last changed by ${settings.data.updatedBy}` : ''}
          </div>
        </div>
      </header>

      <form
        onSubmit={submit}
        noValidate
        className="grid max-w-[1000px] grid-cols-1 items-start gap-4 lg:grid-cols-2"
      >
        <section className="card bg-bg p-5 shadow-sm">
          <h2 className="mb-3.5 text-[21px]">Shop details</h2>

          <Field label="Shop name">
            {p => <input {...p} className="input" value={value.shopName} onChange={e => set('shopName', e.target.value)} />}
          </Field>
          <Field label="City">
            {p => <input {...p} className="input" value={value.city} onChange={e => set('city', e.target.value)} />}
          </Field>
          <Field label="Address">
            {p => <input {...p} className="input" value={value.addressLine} onChange={e => set('addressLine', e.target.value)} />}
          </Field>
          <Field label="WhatsApp number" hint="03xx xxx xxxx">
            {p => <input {...p} className="input" type="tel" value={value.whatsAppNumber}
                         onChange={e => set('whatsAppNumber', e.target.value)} />}
          </Field>
          <Field label="Opening hours">
            {p => <input {...p} className="input" value={value.openingHours ?? ''}
                         onChange={e => set('openingHours', e.target.value)} />}
          </Field>
          <Field label="Banner text" hint="The strip across the top of the shop">
            {p => <textarea {...p} className="input" rows={2} value={value.bannerText ?? ''}
                            onChange={e => set('bannerText', e.target.value)} />}
          </Field>
        </section>

        <div className="flex flex-col gap-4">
          <section className="card bg-bg p-5 shadow-sm">
            <h2 className="mb-3 text-[21px]">Payment &amp; delivery</h2>

            <Toggle
              label="Cash on delivery" note="Rider collects at the door"
              checked={value.cashOnDeliveryEnabled} onChange={v => set('cashOnDeliveryEnabled', v)}
            />
            <Toggle
              label="WhatsApp orders" note={`Quotes and photos on ${value.whatsAppNumber}`}
              checked={value.whatsAppOrdersEnabled} onChange={v => set('whatsAppOrdersEnabled', v)}
            />
            <Toggle
              label="Reserve, pay in shop" note="Holds cloth for 48 hours"
              checked={value.reserveInShopEnabled} onChange={v => set('reserveInShopEnabled', v)}
            />
            <Toggle
              label="Card payment" note="Not set up yet"
              checked={value.cardPaymentEnabled} onChange={v => set('cardPaymentEnabled', v)}
            />

            {noPaymentMethod && (
              <p className="field-error mt-2.5">
                Leave at least one way for customers to pay.
              </p>
            )}
          </section>

          <section className="card bg-bg p-5 shadow-sm">
            <h2 className="mb-2 text-[21px]">Delivery charge</h2>

            <Field label={`Inside ${value.city} (Rs)`}>
              {p => <input {...p} className="input" type="number" min={0} value={value.deliveryCharge}
                           onChange={e => set('deliveryCharge', Number(e.target.value))} />}
            </Field>
            <Field label="Free over (Rs)">
              {p => <input {...p} className="input" type="number" min={0} value={value.freeDeliveryThreshold}
                           onChange={e => set('freeDeliveryThreshold', Number(e.target.value))} />}
            </Field>
            <p className="mb-0 mt-2 text-xs text-neutral-600">
              The storefront shows the banner and works out free delivery from these.
            </p>
          </section>

          {error && (
            <div role="alert" className="rounded-md bg-accent-100 px-4 py-3 text-sm text-accent-800">{error}</div>
          )}

          {saved && (
            <div role="status" className="rounded-md bg-accent-2-100 px-4 py-3 text-sm text-accent-2-800">Settings saved.</div>
          )}

          <div className="flex gap-2.5">
            <button type="submit" className="btn btn-primary" disabled={save.isPending || !form}>
              {save.isPending ? 'Saving…' : 'Save settings'}
            </button>
            {form && (
              <button type="button" className="btn btn-secondary" onClick={() => { setForm(null); setError(null); }}>
                Discard changes
              </button>
            )}
          </div>
        </div>
      </form>
    </>
  );
}

/**
 * A switch. A real checkbox underneath, so it is keyboard-operable and
 * announced as a checkbox — the design's div-with-a-knob was neither.
 */
function Toggle({ label, note, checked, onChange }: {
  label: string; note: string; checked: boolean; onChange: (v: boolean) => void;
}) {
  return (
    <label className="flex cursor-pointer items-center justify-between gap-3.5 py-2">
      <span>
        <span className="block text-sm font-semibold">{label}</span>
        <span className="block text-xs text-neutral-600">{note}</span>
      </span>

      <input
        type="checkbox" checked={checked} onChange={e => onChange(e.target.checked)}
        className="sr-only toggle-input"
      />
      <span className="toggle-track" aria-hidden="true"><span className="toggle-knob" /></span>
    </label>
  );
}
