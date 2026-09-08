import { useState } from 'react';
import { Link } from 'react-router-dom';
import { ErrorState, Loading } from '../../components/Feedback';
import { fabricBackground } from '../../lib/fabric';
import { formatPkr, formatShortDate } from '../../lib/format';
import { useAdminProducts, useArchiveProduct, useRestoreProduct } from '../../hooks/adminQueries';
import { useAuth } from '../../state/auth';
import type { AdminProduct } from '../../types/admin';

/** The product list the editor is opened from. */
export function Products() {
  const [search, setSearch]   = useState('');
  const [archived, setArchived] = useState(false);
  const [error, setError]     = useState<string | null>(null);

  const { isOwner } = useAuth();

  const products = useAdminProducts({
    search: search || undefined,
    archivedOnly: archived || undefined,
    pageSize: 50,
  });

  const archive = useArchiveProduct();
  const restore = useRestoreProduct();
  const busy = archive.isPending || restore.isPending;

  //  Both can fail for reasons the shopkeeper needs told: a restored product's
  //  name may have been taken while it was away, or its category closed.
  function run(action: typeof archive, productId: number) {
    setError(null);
    action.mutate(productId, { onError: e => setError(e.message) });
  }

  return (
    <>
      <header className="mb-5 flex flex-wrap items-center justify-between gap-5">
        <div>
          <h1 className="m-0 text-4xl">Products</h1>
          <div className="mt-[3px] text-sm text-neutral-600">
            {products.data
              ? `${products.data.totalCount} ${archived ? 'archived' : 'in the catalogue'}`
              : 'Loading…'}
          </div>
        </div>
        <Link to="/shop-panel/products/new" className="btn btn-primary">+ New product</Link>
      </header>

      <section className="card mb-4 bg-bg p-5 shadow-sm">
        <div className="mb-3.5 flex flex-wrap items-end gap-2.5">
          <label className="min-w-[200px] flex-[1_1_240px]">
            <span className="sr-only">Search products</span>
            <input className="input" type="search" placeholder="Search products"
                   value={search} onChange={e => setSearch(e.target.value)} />
          </label>

          {/*  Archived products are a separate view rather than a row style:
              they are not sellable, and mixing them into the list makes the
              catalogue look bigger than the shop is.                        */}
          <div className="flex gap-2" role="group" aria-label="Which products to show">
            <button type="button" className="chip" aria-pressed={!archived}
                    onClick={() => { setArchived(false); setError(null); }}>
              In the shop
            </button>
            <button type="button" className="chip" aria-pressed={archived}
                    onClick={() => { setArchived(true); setError(null); }}>
              Archived
            </button>
          </div>
        </div>

        {error && <div role="alert" className="field-error mb-3">{error}</div>}

        {products.isPending && <Loading label="Loading products…" />}
        {products.isError && <ErrorState title="Could not load products" onRetry={() => products.refetch()} />}

        {products.data && products.data.items.length === 0 && (
          <p className="py-8 text-center text-neutral-600">
            {archived
              ? 'Nothing is archived. Removing a product from the shop puts it here.'
              : 'No products match that search.'}
          </p>
        )}

        {products.data && products.data.items.length > 0 && (
          <div className="overflow-x-auto">
            <table className="table">
              <thead>
                <tr>
                  <th>Product</th><th>Category</th><th>Price</th><th>Stock</th><th>Sold</th>
                  <th>{archived ? 'Archived' : 'Shown'}</th><th></th>
                </tr>
              </thead>
              <tbody>
                {products.data.items.map(p => (
                  <tr key={p.productId}>
                    <td>
                      <div className="flex items-center gap-3">
                        <div
                          className="washed h-[38px] w-[38px] flex-none rounded-xl" aria-hidden="true"
                          style={{ background: fabricBackground(p.swatchColorValue, p.swatchWeave) }}
                        />
                        <span className="font-semibold">{p.name}</span>
                      </div>
                    </td>
                    <td>{p.categoryName}</td>
                    <td className="whitespace-nowrap">{formatPkr(p.price)}</td>
                    <td>{p.stock}</td>
                    <td>{p.soldCount}</td>
                    <td className="whitespace-nowrap">
                      {archived
                        ? <ArchivedNote product={p} />
                        : (
                          <span className={p.isActive ? 'tag tag-accent-2' : 'tag tag-neutral'}>
                            {p.isActive ? 'In the shop' : 'Hidden'}
                          </span>
                        )}
                    </td>
                    <td>
                      <div className="flex gap-3 whitespace-nowrap">
                        {!archived && (
                          <Link to={`/shop-panel/products/${p.productId}`} aria-label={`Edit ${p.name}`}>
                            Edit
                          </Link>
                        )}

                        {/*  Removing and restoring change what the shop sells,
                            so both are the owner's to make — the API refuses
                            them for staff either way.                        */}
                        {isOwner && (archived ? (
                          <button type="button" className="btn btn-ghost px-0" disabled={busy}
                                  aria-label={`Bring ${p.name} back to the shop`}
                                  onClick={() => run(restore, p.productId)}>
                            Bring back
                          </button>
                        ) : (
                          <button type="button" className="btn btn-ghost px-0 text-accent-800" disabled={busy}
                                  aria-label={`Archive ${p.name}`}
                                  onClick={() => run(archive, p.productId)}>
                            Archive
                          </button>
                        ))}
                      </div>
                    </td>
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

/** When it was archived and by whom — the questions asked when something is missing. */
function ArchivedNote({ product }: { product: AdminProduct }) {
  return (
    <span className="text-xs text-neutral-600">
      {formatShortDate(product.deletedAt)}
      {product.deletedBy ? ` · ${product.deletedBy}` : ''}
    </span>
  );
}
