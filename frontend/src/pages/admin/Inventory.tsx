import { useState } from 'react';
import { ErrorState, Loading } from '../../components/Feedback';
import { fabricBackground } from '../../lib/fabric';
import { formatPkr } from '../../lib/format';
import { useAdjustStock, useInventory } from '../../hooks/adminQueries';
import { ApiError } from '../../lib/api';
import type { InventoryRow } from '../../types/admin';

/**
 * Frame 10 — Inventory.
 *
 * The stock label ("Low — reorder", "Plenty") is decided by the database, not
 * recomputed here, so the panel and any future report agree on what "low" means.
 */
export function Inventory() {
  const [search, setSearch] = useState('');
  const [lowOnly, setLowOnly] = useState(false);
  const [adjusting, setAdjusting] = useState<InventoryRow | null>(null);

  const inventory = useInventory({ search: search || undefined, lowStockOnly: lowOnly });

  return (
    <>
      <header className="mb-5 flex flex-wrap items-center justify-between gap-5">
        <div>
          <h1 className="m-0 text-4xl">Inventory</h1>
          <div className="mt-[3px] text-sm text-neutral-600">
            {inventory.data
              ? `Stock counts across ${inventory.data.productCount} products · ${inventory.data.lowStockCount} low`
              : 'Loading…'}
          </div>
        </div>
      </header>

      <section className="card mb-4 bg-bg p-5 shadow-sm">
        <div className="mb-3.5 flex flex-wrap items-end gap-2.5">
          <label className="min-w-[200px] flex-[1_1_240px]">
            <span className="sr-only">Search products</span>
            <input
              className="input" type="search" placeholder="Search products"
              value={search} onChange={e => setSearch(e.target.value)}
            />
          </label>

          <button
            type="button" className="chip" aria-pressed={lowOnly}
            onClick={() => setLowOnly(!lowOnly)}
          >
            Low stock only
          </button>
        </div>

        {inventory.isPending && <Loading label="Counting stock…" />}
        {inventory.isError && <ErrorState title="Could not load inventory" onRetry={() => inventory.refetch()} />}

        {inventory.data && (
          <div className="overflow-x-auto">
            <table className="table">
              <thead>
                <tr>
                  <th>Product</th><th>Category</th><th>Price</th>
                  <th className="w-60">In stock</th><th>Status</th><th></th>
                </tr>
              </thead>
              <tbody>
                {inventory.data.items.map(row => {
                  // The bar is relative to three times the low-stock threshold —
                  // a fixed ceiling would peg everything at full for a shop that
                  // stocks 50 sheets and 2 umbrellas.
                  const ceiling = Math.max(row.lowStockAt * 3, 1);
                  const pct = Math.min(100, Math.round((row.stock / ceiling) * 100));
                  const low = row.stock < row.lowStockAt;

                  return (
                    <tr key={row.productId}>
                      <td>
                        <div className="flex items-center gap-3">
                          <div
                            className="washed h-[38px] w-[38px] flex-none rounded-xl" aria-hidden="true"
                            style={{ background: fabricBackground(row.swatchColorValue, row.swatchWeave) }}
                          />
                          <span className="font-semibold">{row.name}</span>
                        </div>
                      </td>
                      <td>{row.categoryName}</td>
                      <td className="whitespace-nowrap">{formatPkr(row.price)}</td>
                      <td>
                        <div className="flex items-center gap-2.5">
                          <div
                            className="h-2 min-w-[60px] flex-1 overflow-hidden rounded-full bg-neutral-300"
                            role="meter"
                            aria-valuenow={row.stock}
                            aria-valuemin={0}
                            aria-valuemax={ceiling}
                            aria-label={`${row.name} stock`}
                          >
                            <div
                              className={`h-full rounded-full ${low ? 'bg-accent' : 'bg-accent-2-500'}`}
                              style={{ width: `${pct}%` }}
                            />
                          </div>
                          <span className="w-[30px] text-right text-[13px]">{row.stock}</span>
                        </div>
                      </td>
                      <td>
                        <span className={low ? 'tag tag-accent' : 'tag tag-accent-2'}>{row.stockLabel}</span>
                      </td>
                      <td>
                        <button
                          type="button" className="btn btn-ghost"
                          onClick={() => setAdjusting(row)}
                          aria-label={`Adjust stock for ${row.name}`}
                        >
                          Adjust
                        </button>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </section>

      {adjusting && <AdjustDialog row={adjusting} onClose={() => setAdjusting(null)} />}
    </>
  );
}

/**
 * Stock adjustment. Signed, with a reason, because every movement is written to
 * the audit table — "where did four sheets go" should always have an answer.
 */
function AdjustDialog({ row, onClose }: { row: InventoryRow; onClose: () => void }) {
  const [delta, setDelta] = useState('');
  const [reason, setReason] = useState('');
  const [error, setError] = useState<string | null>(null);

  const adjust = useAdjustStock(row.productId);
  const parsed = Number(delta);
  const resulting = row.stock + (Number.isFinite(parsed) ? parsed : 0);

  function submit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);

    if (!Number.isFinite(parsed) || parsed === 0) {
      setError('Enter how many pieces to add or remove.');
      return;
    }
    if (reason.trim().length < 3) {
      setError('Give a reason — it goes on the stock record.');
      return;
    }
    if (resulting < 0) {
      setError(`There are only ${row.stock} in stock.`);
      return;
    }

    adjust.mutate(
      { delta: parsed, reason: reason.trim() },
      {
        onSuccess: onClose,
        onError: (e: unknown) => setError(e instanceof ApiError ? e.message : 'Could not adjust the stock.'),
      },
    );
  }

  return (
    <div
      className="fixed inset-0 z-50 grid place-items-center bg-neutral-900/50 p-4"
      role="dialog"
      aria-modal="true"
      aria-label={`Adjust stock for ${row.name}`}
      onClick={e => { if (e.target === e.currentTarget) onClose(); }}
    >
      <form
        className="flex w-[min(440px,100%)] flex-col gap-3 rounded-card bg-bg p-6 shadow-lg"
        onSubmit={submit}
        noValidate
      >
        <h2 className="m-0 font-heading text-[22px]">{row.name}</h2>
        <p className="m-0 text-sm text-neutral-700">
          {row.stock} in stock now.
          {delta && Number.isFinite(parsed) && parsed !== 0 && (
            <> After this: <strong>{resulting}</strong>.</>
          )}
        </p>

        <label>
          <span className="mb-1 block text-xs">
            Add or remove (use a minus to remove)
          </span>
          <input
            className="input" type="number" inputMode="numeric" autoFocus
            placeholder="e.g. 12 or -3"
            value={delta} onChange={e => setDelta(e.target.value)}
          />
        </label>

        <label>
          <span className="mb-1 block text-xs">Reason</span>
          <input
            className="input" placeholder="New cloth from the mill"
            value={reason} onChange={e => setReason(e.target.value)}
          />
        </label>

        {error && <div role="alert" className="field-error">{error}</div>}

        <div className="mt-2 flex justify-end gap-2">
          <button type="button" className="btn btn-secondary" onClick={onClose}>Cancel</button>
          <button type="submit" className="btn btn-primary" disabled={adjust.isPending}>
            {adjust.isPending ? 'Saving…' : 'Save adjustment'}
          </button>
        </div>
      </form>
    </div>
  );
}
