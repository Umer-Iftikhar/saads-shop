import { useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { ErrorState, Loading } from '../../components/Feedback';
import { StatusTag } from '../../components/admin/StatusTag';
import { formatPkr, formatShortDate, toDateInput } from '../../lib/format';
import { hasErrors, validateDateRange } from '../../lib/validation';
import type { FieldErrors } from '../../lib/validation';
import { useOrders } from '../../hooks/adminQueries';

const STATUSES = ['Placed', 'Measuring', 'Stitching', 'Ready', 'Delivered', 'Cancelled'];

/**
 * Frame 08 — Orders.
 *
 * The date range is validated here before anything is sent, by the same rules
 * the server's `[DateRange]` attribute applies and the stored procedure checks
 * again. Three layers, and this one exists so a shopkeeper who types the dates
 * backwards is told immediately rather than after a round trip.
 */
export function Orders() {
  const [params, setParams] = useSearchParams();

  const status = params.get('status') ?? '';
  const page   = Number(params.get('page') ?? '1');

  // The filter form is local state; only a valid range reaches the URL, and
  // therefore the query.
  const [search, setSearch]     = useState(params.get('search') ?? '');
  const [fromDate, setFromDate] = useState(params.get('fromDate') ?? '');
  const [toDate, setToDate]     = useState(params.get('toDate') ?? '');
  const [errors, setErrors]     = useState<FieldErrors>({});

  const orders = useOrders({
    status: status || undefined,
    search: params.get('search') || undefined,
    fromDate: params.get('fromDate') || undefined,
    toDate: params.get('toDate') || undefined,
    page,
    pageSize: 25,
  });

  function applyFilters(event: React.FormEvent) {
    event.preventDefault();

    // Same rules as the server: a year at most, nothing in the future,
    // start on or before end.
    const found = validateDateRange(fromDate, toDate, {
      maxSpanDays: 366,
      allowFuture: false,
      allowOpenRange: true,
    });

    setErrors(found);
    if (hasErrors(found)) return;

    const next = new URLSearchParams(params);
    for (const [key, value] of Object.entries({ search, fromDate, toDate })) {
      if (value) next.set(key, value); else next.delete(key);
    }
    next.delete('page');
    setParams(next);
  }

  function setStatus(value: string) {
    const next = new URLSearchParams(params);
    if (value) next.set('status', value); else next.delete('status');
    next.delete('page');
    setParams(next);
  }

  function clearAll() {
    setSearch(''); setFromDate(''); setToDate(''); setErrors({});
    setParams(new URLSearchParams());
  }

  const today = toDateInput(new Date());
  const data = orders.data;

  return (
    <>
      <header className="mb-5 flex flex-wrap items-center justify-between gap-5">
        <div>
          <h1 className="m-0 text-4xl">Orders</h1>
          <div className="mt-[3px] text-sm text-neutral-600">
            {orders.isPending
              ? 'Loading…'
              : `${data?.totalCount ?? 0} ${data?.totalCount === 1 ? 'order' : 'orders'}` +
                `${data?.needsAttentionCount ? ` · ${data.needsAttentionCount} need attention` : ''}`}
          </div>
        </div>
      </header>

      <section className="card mb-4 bg-bg p-5 shadow-sm">
        <form onSubmit={applyFilters} noValidate>
          <div className="mb-3.5 flex flex-wrap items-end gap-2.5">
            <label className="min-w-[200px] flex-[1_1_240px]">
              <span className="mb-1 block text-xs text-neutral-700">
                Search
              </span>
              <input
                className="input"
                type="search"
                placeholder="Name, number or order reference"
                value={search}
                onChange={e => setSearch(e.target.value)}
              />
            </label>

            <label>
              <span className="mb-1 block text-xs text-neutral-700">
                From
              </span>
              <input
                className="input"
                type="date"
                max={today}
                value={fromDate}
                onChange={e => setFromDate(e.target.value)}
                aria-invalid={errors.fromDate ? true : undefined}
                aria-describedby={errors.fromDate ? 'from-error' : undefined}
              />
            </label>

            <label>
              <span className="mb-1 block text-xs text-neutral-700">
                To
              </span>
              <input
                className="input"
                type="date"
                max={today}
                value={toDate}
                onChange={e => setToDate(e.target.value)}
                aria-invalid={errors.toDate ? true : undefined}
                aria-describedby={errors.toDate ? 'to-error' : undefined}
              />
            </label>

            <button type="submit" className="btn btn-primary">Apply</button>
            {(params.toString() !== '') && (
              <button type="button" className="btn btn-secondary" onClick={clearAll}>Clear</button>
            )}
          </div>

          {errors.fromDate && <div id="from-error" className="field-error">{errors.fromDate}</div>}
          {errors.toDate   && <div id="to-error"   className="field-error">{errors.toDate}</div>}
        </form>

        <div className="my-3.5 flex flex-wrap gap-2" role="group" aria-label="Filter by status">
          <button type="button" className="chip" aria-pressed={!status} onClick={() => setStatus('')}>All</button>
          {STATUSES.map(value => (
            <button
              key={value}
              type="button"
              className="chip"
              aria-pressed={status === value}
              onClick={() => setStatus(value)}
            >
              {value}
            </button>
          ))}
        </div>

        {orders.isPending && <Loading label="Loading orders…" />}

        {orders.isError && (
          <ErrorState title="Could not load orders" onRetry={() => orders.refetch()} />
        )}

        {data && data.items.length === 0 && (
          <p className="py-8 text-center text-neutral-600">
            No orders match those filters.
          </p>
        )}

        {data && data.items.length > 0 && (
          <div className="overflow-x-auto">
            <table className="table">
              <thead>
                <tr>
                  <th>Order</th><th>Date</th><th>Customer</th><th>Items</th>
                  <th>Total</th><th>Payment</th><th>Status</th><th></th>
                </tr>
              </thead>
              <tbody>
                {data.items.map(order => (
                  <tr key={order.orderId}>
                    <td className="font-bold">{order.reference}</td>
                    <td className="whitespace-nowrap">{formatShortDate(order.placedAt)}</td>
                    <td>{order.customerName}</td>
                    <td className="max-w-[220px] truncate">
                      {order.itemSummary}
                    </td>
                    <td className="whitespace-nowrap">{formatPkr(order.total)}</td>
                    <td className="whitespace-nowrap">{order.paymentMethod.replace(/([A-Z])/g, ' $1').trim()}</td>
                    <td><StatusTag status={order.status} /></td>
                    <td>
                      {/*  The link carries the reference in its accessible name,
                          so a screen reader hears "Open SS-2419" rather than a
                          column of identical "Open" links.                    */}
                      <Link to={`/shop-panel/orders/${order.orderId}`} aria-label={`Open ${order.reference}`}>
                        Open →
                      </Link>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {data && data.totalPages > 1 && (
          <nav aria-label="Pages" className="mt-5 flex justify-center gap-2.5">
            <button
              type="button" className="btn btn-secondary" disabled={page <= 1}
              onClick={() => { const n = new URLSearchParams(params); n.set('page', String(page - 1)); setParams(n); }}
            >
              ← Previous
            </button>
            <span className="self-center text-sm text-neutral-700">
              Page {data.page} of {data.totalPages}
            </span>
            <button
              type="button" className="btn btn-secondary" disabled={page >= data.totalPages}
              onClick={() => { const n = new URLSearchParams(params); n.set('page', String(page + 1)); setParams(n); }}
            >
              Next →
            </button>
          </nav>
        )}
      </section>
    </>
  );
}
