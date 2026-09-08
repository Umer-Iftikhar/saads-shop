import { useState } from 'react';
import { ErrorState, Loading } from '../../components/Feedback';
import { formatPhone, formatPkr, formatShortDate, toDateInput } from '../../lib/format';
import { hasErrors, validateDateRange } from '../../lib/validation';
import type { FieldErrors } from '../../lib/validation';
import { useCustomers } from '../../hooks/adminQueries';

/**
 * Frame 13 — Customers.
 *
 * The "last ordered between" range uses the same validator as the order search,
 * with a wider span allowed: looking back five years over a customer list is a
 * reasonable question, where five years of orders is a report.
 */
export function Customers() {
  const [search, setSearch]     = useState('');
  const [fromDate, setFromDate] = useState('');
  const [toDate, setToDate]     = useState('');
  const [errors, setErrors]     = useState<FieldErrors>({});
  const [applied, setApplied]   = useState<{ search?: string; fromDate?: string; toDate?: string }>({});

  const customers = useCustomers({ ...applied, pageSize: 50 });

  function apply(event: React.FormEvent) {
    event.preventDefault();

    const found = validateDateRange(fromDate, toDate, {
      maxSpanDays: 1830,          // five years
      allowFuture: false,
      allowOpenRange: true,
    });

    setErrors(found);
    if (hasErrors(found)) return;

    setApplied({
      search: search || undefined,
      fromDate: fromDate || undefined,
      toDate: toDate || undefined,
    });
  }

  const today = toDateInput(new Date());

  return (
    <>
      <header className="mb-5 flex flex-wrap items-center justify-between gap-5">
        <div>
          <h1 className="m-0 text-4xl">Customers</h1>
          <div className="mt-[3px] text-sm text-neutral-600">
            Repeat buyers and the areas they order from
          </div>
        </div>
      </header>

      <section className="card mb-4 bg-bg p-5 shadow-sm">
        <form onSubmit={apply} noValidate>
          <div className="mb-3.5 flex flex-wrap items-end gap-2.5">
            <label className="min-w-[180px] flex-[1_1_220px]">
              <span className="mb-1 block text-xs text-neutral-700">Search</span>
              <input className="input" type="search" placeholder="Name, number or area"
                     value={search} onChange={e => setSearch(e.target.value)} />
            </label>

            <label>
              <span className="mb-1 block text-xs text-neutral-700">
                Last ordered from
              </span>
              <input className="input" type="date" max={today} value={fromDate}
                     onChange={e => setFromDate(e.target.value)}
                     aria-invalid={errors.fromDate ? true : undefined}
                     aria-describedby={errors.fromDate ? 'cust-from-error' : undefined} />
            </label>

            <label>
              <span className="mb-1 block text-xs text-neutral-700">to</span>
              <input className="input" type="date" max={today} value={toDate}
                     onChange={e => setToDate(e.target.value)}
                     aria-invalid={errors.toDate ? true : undefined}
                     aria-describedby={errors.toDate ? 'cust-to-error' : undefined} />
            </label>

            <button type="submit" className="btn btn-primary">Apply</button>
          </div>

          {errors.fromDate && <div id="cust-from-error" className="field-error">{errors.fromDate}</div>}
          {errors.toDate   && <div id="cust-to-error"   className="field-error">{errors.toDate}</div>}
        </form>

        {customers.isPending && <Loading label="Loading customers…" />}
        {customers.isError && <ErrorState title="Could not load customers" onRetry={() => customers.refetch()} />}

        {customers.data && customers.data.items.length === 0 && (
          <p className="py-8 text-center text-neutral-600">
            No customers match that.
          </p>
        )}

        {customers.data && customers.data.items.length > 0 && (
          <div className="mt-3.5 overflow-x-auto">
            <table className="table">
              <thead>
                <tr><th>Customer</th><th>Phone</th><th>Area</th><th>Orders</th><th>Spent</th><th>Last order</th></tr>
              </thead>
              <tbody>
                {customers.data.items.map(c => (
                  <tr key={c.customerId}>
                    <td className="font-semibold">{c.name}</td>
                    <td className="whitespace-nowrap">{formatPhone(c.phone)}</td>
                    <td>{c.area ?? '—'}</td>
                    <td>{c.orderCount}</td>
                    <td className="whitespace-nowrap">{formatPkr(c.totalSpent)}</td>
                    <td className="whitespace-nowrap">{formatShortDate(c.lastOrderAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </>
  );
}
