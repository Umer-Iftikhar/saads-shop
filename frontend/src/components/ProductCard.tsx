import { Link } from 'react-router-dom';
import { fabricBackground } from '../lib/fabric';
import { formatPkr } from '../lib/format';
import type { ProductSummary } from '../types/api';

/**
 * A product card. `size` picks between the three treatments in the design:
 * the large featured cards on the home hero row, the listing grid, and the
 * smaller everyday tiles.
 */
export function ProductCard({ product, size = 'listing' }: {
  product: ProductSummary;
  size?: 'featured' | 'listing' | 'compact';
}) {
  const compact = size === 'compact';

  // The cloth panel and the title step down with the treatment. Written as a
  // lookup rather than three sets of conditional classes, which is where this
  // sort of thing usually goes wrong.
  const swatchHeight = { featured: 'h-[230px]', listing: 'h-[190px]', compact: 'h-[140px]' }[size];
  const titleSize    = { featured: 'text-[23px]', listing: 'text-xl', compact: 'text-[17px]' }[size];

  return (
    <Link
      to={`/product/${product.slug}`}
      className={`card gap-0 overflow-hidden p-0 text-text shadow-sm hover:no-underline
                  ${compact ? 'bg-neutral-200' : 'bg-surface'}`}
    >
      <div
        className={`washed ${swatchHeight}`}
        style={{ background: fabricBackground(product.swatchColorValue, product.swatchWeave) }}
        // Decorative: the cloth is described by the product name and the swatch
        // picker on the detail page. Announcing "woven terracotta" here would
        // just add noise before every card title.
        aria-hidden="true"
      />

      <div className={compact ? 'px-[17px] pb-[17px] pt-[15px]' : 'px-[22px] pb-[22px] pt-5'}>
        {product.kicker && !compact && <div className="card-kicker">{product.kicker}</div>}

        <div className={`mt-1 font-heading leading-[1.1] ${titleSize}`}>
          {product.name}
        </div>

        {compact ? (
          <div className="mb-2 mt-1 text-[13px] text-neutral-600">
            {product.categoryName}
          </div>
        ) : (
          product.blurb && (
            <p className={`mb-0 mt-1.5 text-neutral-700 ${size === 'featured' ? 'text-sm' : 'text-[13px]'}`}>
              {product.blurb}
            </p>
          )
        )}

        <div className={`flex items-center justify-between gap-2.5 ${compact ? '' : 'mt-3'}`}>
          <span className={`font-bold ${size === 'featured' ? 'text-lg' : 'text-[17px]'}`}>
            {formatPkr(product.price)}
          </span>

          {!product.inStock
            ? <span className="tag tag-neutral">Out of stock</span>
            : product.pieces && !compact
              ? <span className={size === 'featured' ? 'tag tag-neutral' : 'tag tag-accent-2'}>{product.pieces}</span>
              : null}
        </div>
      </div>
    </Link>
  );
}
