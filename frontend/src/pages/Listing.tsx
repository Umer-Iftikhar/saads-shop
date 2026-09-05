import { useMemo } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { ProductCard } from '../components/ProductCard';
import { CardSkeleton, EmptyState, ErrorState } from '../components/Feedback';
import { useCategories, useProducts } from '../hooks/queries';

/**
 * Frame 02 — category listing.
 *
 * Serves both `/wedding-sets` and `/bistar-parde`; the latter is the design's
 * "everything else" view, so it shows the whole catalogue with the category
 * chips as filters.
 */
export function Listing({ fixedCategory, title, subtitle }: {
  fixedCategory?: string;
  title?: string;
  subtitle?: string;
}) {
  const params = useParams<{ slug?: string }>();
  const [search, setSearch] = useSearchParams();

  const categories = useCategories();

  // A fixed category (the Wedding sets page) wins; otherwise the chips drive it
  // through the query string, so a filtered view is a shareable URL.
  const selected = fixedCategory ?? params.slug ?? search.get('category') ?? undefined;
  const page = Number(search.get('page') ?? '1');

  const products = useProducts({ category: selected, page, pageSize: 12 });

  const heading = useMemo(() => {
    if (title) return title;
    const match = categories.data?.find(c => c.slug === selected);
    return match?.name ?? 'Everything in the shop';
  }, [title, categories.data, selected]);

  function setCategory(slug: string | null) {
    const next = new URLSearchParams(search);
    if (slug) next.set('category', slug); else next.delete('category');
    // Changing the filter must reset paging, or someone on page 3 of curtains
    // lands on an empty page 3 of umbrellas.
    next.delete('page');
    setSearch(next, { replace: true });
  }

  function goToPage(target: number) {
    const next = new URLSearchParams(search);
    next.set('page', String(target));
    setSearch(next);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  const total = products.data?.totalCount ?? 0;

  return (
    <main id="main" className="page py-10">
      <h1 className="display-page mb-2 mt-0">{heading}</h1>

      <p className="mb-7 mt-0 text-base text-neutral-700">
        {subtitle ?? (products.isPending
          ? 'Loading…'
          : `${total} ${total === 1 ? 'item' : 'items'} · stitching and delivery inside Rawalpindi`)}
      </p>

      {!fixedCategory && (
        <div className="mb-7 flex flex-wrap gap-2.5" role="group" aria-label="Filter by category">
          <FilterChip active={!selected} onClick={() => setCategory(null)}>All</FilterChip>
          {categories.data?.map(category => (
            <FilterChip
              key={category.categoryId}
              active={selected === category.slug}
              onClick={() => setCategory(category.slug)}
            >
              {category.name}
            </FilterChip>
          ))}
        </div>
      )}

      <div className="grid grid-cols-1 gap-[22px] md:grid-cols-2 2xl:grid-cols-3">
        {products.isPending && <CardSkeleton count={6} height={330} />}

        {products.isError && (
          <div className="col-span-full">
            <ErrorState
              title="Could not load these products"
              detail="This is our end, not yours. Try again in a moment."
              onRetry={() => products.refetch()}
            />
          </div>
        )}

        {products.data?.items.map(product => (
          <ProductCard key={product.productId} product={product} size="listing" />
        ))}
      </div>

      {products.data && products.data.items.length === 0 && (
        <EmptyState
          title="Nothing here yet"
          detail="Nothing in this part of the shop right now. The wedding sets are where the new cloth lands first."
          action={<Link to="/wedding-sets" className="btn btn-primary">See wedding sets</Link>}
        />
      )}

      {products.data && products.data.totalPages > 1 && (
        <nav aria-label="Pages" className="mt-10 flex justify-center gap-2.5">
          <button
            type="button"
            className="btn btn-secondary"
            disabled={page <= 1}
            onClick={() => goToPage(page - 1)}
          >
            ← Previous
          </button>

          <span className="self-center text-sm text-neutral-700">
            Page {products.data.page} of {products.data.totalPages}
          </span>

          <button
            type="button"
            className="btn btn-secondary"
            disabled={page >= products.data.totalPages}
            onClick={() => goToPage(page + 1)}
          >
            Next →
          </button>
        </nav>
      )}
    </main>
  );
}

function FilterChip({ active, onClick, children }: {
  active: boolean;
  onClick: () => void;
  children: React.ReactNode;
}) {
  return (
    <button type="button" className="chip" onClick={onClick} aria-pressed={active}>
      {children}
    </button>
  );
}
