import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { ErrorState, Loading } from '../../components/Feedback';
import { StatusTag } from '../../components/admin/StatusTag';
import { Field } from '../../components/Field';
import { fabricBackground } from '../../lib/fabric';
import { formatPhone, formatPkr, formatShortDate, whatsAppLink } from '../../lib/format';
import { useOrder, useSaveMeasurements, useUpdateOrderStatus } from '../../hooks/adminQueries';
import { ApiError } from '../../lib/api';

/**
 * Frame 09 — Order detail.
 *
 * The status buttons offer only moves the database will actually accept. The
 * procedure enforces the transition table regardless, but offering a button
 * that always fails is a worse experience than not offering it.
 */
const NEXT_STATUS: Record<string, string[]> = {
  Placed:    ['Measuring', 'Cancelled'],
  Measuring: ['Stitching', 'Cancelled'],
  Stitching: ['Ready', 'Cancelled'],
  Ready:     ['Delivered', 'Cancelled'],
  Delivered: [],
  Cancelled: [],
};

export function OrderDetail() {
  const { orderId: param } = useParams<{ orderId: string }>();
  const orderId = Number(param);

  const order = useOrder(orderId);
  const updateStatus = useUpdateOrderStatus(orderId);
  const saveMeasurements = useSaveMeasurements(orderId);

  const [showMeasure, setShowMeasure] = useState(false);
  const [measure, setMeasure] = useState({ bedWidthIn: '', bedLengthIn: '', windowDropIn: '', windowCount: '', takenBy: '', notes: '' });
  const [error, setError] = useState<string | null>(null);

  if (order.isPending) return <Loading label="Loading the order…" />;

  if (order.isError || !order.data) {
    return <ErrorState title="Could not load that order" onRetry={() => order.refetch()} />;
  }

  const o = order.data;
  const moves = NEXT_STATUS[o.status] ?? [];

  function move(status: string) {
    setError(null);
    updateStatus.mutate(
      { status, note: null },
      { onError: (e: unknown) => setError(e instanceof ApiError ? e.message : 'Could not update the order.') },
    );
  }

  function submitMeasurements(event: React.FormEvent) {
    event.preventDefault();
    setError(null);

    // Blank fields are omitted rather than sent as 0 — the procedure treats a
    // zero as out of range, and "not measured" is not "measured as nothing".
    const body: Record<string, unknown> = {};
    if (measure.bedWidthIn)   body.bedWidthIn   = Number(measure.bedWidthIn);
    if (measure.bedLengthIn)  body.bedLengthIn  = Number(measure.bedLengthIn);
    if (measure.windowDropIn) body.windowDropIn = Number(measure.windowDropIn);
    if (measure.windowCount)  body.windowCount  = Number(measure.windowCount);
    if (measure.takenBy)      body.takenBy      = measure.takenBy;
    if (measure.notes)        body.notes        = measure.notes;

    saveMeasurements.mutate(body, {
      onSuccess: () => { setShowMeasure(false); setMeasure({ bedWidthIn: '', bedLengthIn: '', windowDropIn: '', windowCount: '', takenBy: '', notes: '' }); },
      onError: (e: unknown) => setError(e instanceof ApiError ? e.message : 'Could not save the measurements.'),
    });
  }

  return (
    <>
      <Link to="/shop-panel/orders" className="mb-4 inline-block text-sm">
        ← All orders
      </Link>

      {error && (
        <div role="alert" className="card mb-4 bg-accent-100 px-4 py-3 text-sm text-accent-800">
          {error}
        </div>
      )}

      <div className="grid grid-cols-1 items-start gap-4 3xl:grid-cols-[1fr_330px]">
        <section className="card mb-4 bg-bg p-5 shadow-sm">
          <div className="mb-4 flex items-start justify-between gap-4">
            <div>
              <h1 className="m-0 font-heading text-[28px]">{o.reference}</h1>
              <div className="text-[13px] text-neutral-600">
                {formatShortDate(o.placedAt)} · {o.paymentMethod.replace(/([A-Z])/g, ' $1').trim()}
              </div>
            </div>
            <StatusTag status={o.status} />
          </div>

          {o.lines.map(line => (
            <div key={line.orderLineId} className="flex items-center gap-3.5 border-b border-divider py-3">
              <div
                className="washed h-[50px] w-[50px] flex-none rounded-md" aria-hidden="true"
                style={{ background: fabricBackground(line.swatchColorValue, line.swatchWeave) }}
              />
              <div className="min-w-0 flex-1">
                <div className="font-semibold">{line.productName}{line.quantity > 1 && ` ×${line.quantity}`}</div>
                <div className="text-xs text-neutral-600">
                  {[line.bedSize, line.swatchName].filter(Boolean).join(' · ') || '—'}
                </div>
              </div>
              <div className="whitespace-nowrap font-bold">{formatPkr(line.lineTotal)}</div>
            </div>
          ))}

          <div className="flex justify-between pt-3 text-sm">
            <span>Delivery</span>
            <span>{o.deliveryCharge === 0 ? 'Free' : formatPkr(o.deliveryCharge)}</span>
          </div>
          <div className="flex items-baseline justify-between pt-2">
            <span className="text-[13px] text-neutral-600">Total</span>
            <span className="font-heading text-[26px]">{formatPkr(o.total)}</span>
          </div>

          {o.notes && (
            <div className="mt-4 rounded-md bg-neutral-200 px-4 py-3.5 text-[13px]">
              <strong>Customer note:</strong> {o.notes}
            </div>
          )}

          {o.measurements.length > 0 && (
            <div className="mt-4 rounded-md bg-accent-100 px-5 py-4 text-[13px] text-accent-800">
              {o.measurements.map((m, i) => (
                <div key={i}>
                  Measurements:
                  {m.bedWidthIn && m.bedLengthIn ? ` bed ${m.bedLengthIn}×${m.bedWidthIn}in` : ''}
                  {m.windowDropIn ? ` · windows ${m.windowDropIn}in drop` : ''}
                  {m.windowCount ? ` ×${m.windowCount}` : ''}
                  {m.takenBy ? `, taken by ${m.takenBy}` : ''}
                  {` on ${formatShortDate(m.takenAt)}`}
                  {m.notes ? ` — ${m.notes}` : ''}
                </div>
              ))}
            </div>
          )}

          {o.history.length > 0 && (
            <details className="mt-4">
              <summary className="cursor-pointer text-[13px] text-accent-700">
                History ({o.history.length})
              </summary>
              <ul className="m-0 mt-2.5 list-none p-0 text-[13px]">
                {o.history.map((h, i) => (
                  <li key={i} className="border-b border-divider py-1.5">
                    {h.fromStatus ? `${h.fromStatus} → ` : ''}<strong>{h.toStatus}</strong>
                    {h.changedBy ? ` by ${h.changedBy}` : ''} · {formatShortDate(h.changedAt)}
                    {h.note ? ` — ${h.note}` : ''}
                  </li>
                ))}
              </ul>
            </details>
          )}
        </section>

        <div className="flex flex-col gap-4">
          <section className="card bg-bg p-5 shadow-sm">
            <h2 className="mb-2 text-[19px]">Customer</h2>
            <div className="text-[15px] font-semibold">{o.customerName}</div>
            {o.phone && <div className="mt-[3px] text-[13px] text-neutral-700">{formatPhone(o.phone)}</div>}
            <div className="text-[13px] text-neutral-700">{o.deliveryAddress}</div>

            {o.phone && (
              <a
                className="btn btn-secondary btn-block mt-3"
                href={whatsAppLink(o.phone, `Assalam o alaikum! About your order ${o.reference} from Saad's Shop —`)}
                target="_blank"
                rel="noopener noreferrer"
              >
                Message on WhatsApp
              </a>
            )}
          </section>

          <section className="card bg-bg p-5 shadow-sm">
            <h2 className="mb-2.5 text-[19px]">Move it along</h2>

            {moves.length === 0 ? (
              <p className="m-0 text-[13px] text-neutral-600">
                This order is {o.status.toLowerCase()} — nothing left to move.
              </p>
            ) : (
              <div className="flex flex-col gap-2">
                {moves.map(status => (
                  <button
                    key={status}
                    type="button"
                    className={`btn btn-block ${status === 'Cancelled' ? 'btn-ghost' : 'btn-primary'}`}
                    disabled={updateStatus.isPending}
                    onClick={() => move(status)}
                  >
                    {status === 'Cancelled' ? 'Cancel order' : `Mark ${status.toLowerCase()}`}
                  </button>
                ))}

                {!showMeasure && o.status !== 'Delivered' && o.status !== 'Cancelled' && (
                  <button type="button" className="btn btn-secondary btn-block" onClick={() => setShowMeasure(true)}>
                    Record measurements
                  </button>
                )}
              </div>
            )}

            {o.status === 'Cancelled' && (
              <p className="mt-2.5 text-xs text-neutral-600">
                The stock went back on the shelf when this was cancelled.
              </p>
            )}
          </section>

          {showMeasure && (
            <section className="card bg-bg p-5 shadow-sm">
              <h2 className="mb-2.5 text-[19px]">Measurements</h2>

              <form onSubmit={submitMeasurements} noValidate>
                <div className="grid grid-cols-2 gap-2.5">
                  <Field label="Bed width (in)">
                    {p => <input {...p} className="input" type="number" min={1} max={200} step="0.5"
                                 value={measure.bedWidthIn} onChange={e => setMeasure({ ...measure, bedWidthIn: e.target.value })} />}
                  </Field>
                  <Field label="Bed length (in)">
                    {p => <input {...p} className="input" type="number" min={1} max={200} step="0.5"
                                 value={measure.bedLengthIn} onChange={e => setMeasure({ ...measure, bedLengthIn: e.target.value })} />}
                  </Field>
                  <Field label="Window drop (in)">
                    {p => <input {...p} className="input" type="number" min={1} max={300} step="0.5"
                                 value={measure.windowDropIn} onChange={e => setMeasure({ ...measure, windowDropIn: e.target.value })} />}
                  </Field>
                  <Field label="Windows">
                    {p => <input {...p} className="input" type="number" min={0} max={100}
                                 value={measure.windowCount} onChange={e => setMeasure({ ...measure, windowCount: e.target.value })} />}
                  </Field>
                </div>

                <Field label="Taken by">
                  {p => <input {...p} className="input" placeholder="Nasir"
                               value={measure.takenBy} onChange={e => setMeasure({ ...measure, takenBy: e.target.value })} />}
                </Field>

                <Field label="Notes">
                  {p => <textarea {...p} className="input" rows={2}
                                  value={measure.notes} onChange={e => setMeasure({ ...measure, notes: e.target.value })} />}
                </Field>

                <div className="mt-2.5 flex gap-2">
                  <button type="submit" className="btn btn-primary" disabled={saveMeasurements.isPending}>
                    {saveMeasurements.isPending ? 'Saving…' : 'Save'}
                  </button>
                  <button type="button" className="btn btn-secondary" onClick={() => setShowMeasure(false)}>Cancel</button>
                </div>
              </form>
            </section>
          )}
        </div>
      </div>
    </>
  );
}
