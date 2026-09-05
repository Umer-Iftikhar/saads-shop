import { useState } from 'react';
import { Link } from 'react-router-dom';
import { ErrorState, Loading } from '../../components/Feedback';
import { fabricBackground } from '../../lib/fabric';
import { formatPkr } from '../../lib/format';
import { useAdminProducts } from '../../hooks/adminQueries';

/** The product list the editor is opened from. */
export function Products() {
  const [search, setSearch] = useState('');
  const products = useAdminProducts({ search: search || undefined, pageSize: 50 });

  return (
    <>
      <header className="mb-5 flex flex-wrap items-center justify-between gap-5">
        <div>
          <h1 className="m-0 text-4xl">Products</h1>
          <div className="mt-[3px] text-sm text-neutral-600">
            {products.data ? `${products.data.totalCount} in the catalogue` : 'Loading…'}
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
        </div>

        {products.isPending && <Loading label="Loading products…" />}
        {products.isError && <ErrorState title="Could not load products" onRetry={() => products.refetch()} />}

        {products.data && (
          <div className="overflow-x-auto">
            <table className="table">
              <thead>
                <tr><th>Product</th><th>Category</th><th>Price</th><th>Stock</th><th>Sold</th><th>Shown</th><th></th></tr>
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
                    <td>
                      <span className={p.isActive ? 'tag tag-accent-2' : 'tag tag-neutral'}>
                        {p.isActive ? 'In the shop' : 'Hidden'}
                      </span>
                    </td>
                    <td>
                      <Link to={`/shop-panel/products/${p.productId}`} aria-label={`Edit ${p.name}`}>Edit</Link>
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
