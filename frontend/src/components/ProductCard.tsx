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
  const swatchHeight = size === 'featured' ? 230 : size === 'listing' ? 190 : 140;
  const titleSize    = size === 'featured' ? 23  : size === 'listing' ? 20  : 17;

  return (
    <Link
      to={`/product/${product.slug}`}
      className="card elev-sm"
      style={{
        padding: 0,
        overflow: 'hidden',
        gap: 0,
        color: 'inherit',
        background: size === 'compact' ? 'var(--color-neutral-200)' : 'var(--color-surface)',
      }}
    >
      <div
        className="washed"
        style={{ height: swatchHeight, background: fabricBackground(product.swatchColorValue, product.swatchWeave) }}
        // Decorative: the cloth is described by the product name and the swatch
        // picker on the detail page. Announcing "woven terracotta" here would
        // just add noise before every card title.
        aria-hidden="true"
      />

      <div style={{ padding: size === 'compact' ? '15px 17px 17px' : '20px 22px 22px' }}>
        {product.kicker && size !== 'compact' && <div className="card-kicker">{product.kicker}</div>}

        <div style={{ fontFamily: 'var(--font-heading)', fontSize: titleSize, lineHeight: 1.1, marginTop: 5 }}>
          {product.name}
        </div>

        {size === 'compact' ? (
          <div style={{ fontSize: 13, color: 'var(--color-neutral-600)', margin: '4px 0 9px' }}>
            {product.categoryName}
          </div>
        ) : (
          product.blurb && (
            <p style={{ margin: '6px 0 0', fontSize: size === 'featured' ? 14 : 13, color: 'var(--color-neutral-700)' }}>
              {product.blurb}
            </p>
          )
        )}

        <div style={{
          display: 'flex', alignItems: 'center', justifyContent: 'space-between',
          gap: 10, marginTop: size === 'compact' ? 0 : 12,
        }}>
          <span style={{ fontWeight: 700, fontSize: size === 'featured' ? 18 : 17 }}>
            {formatPkr(product.price)}
          </span>

          {!product.inStock
            ? <span className="tag tag-neutral">Out of stock</span>
            : product.pieces && size !== 'compact'
              ? <span className={size === 'featured' ? 'tag tag-neutral' : 'tag tag-accent-2'}>{product.pieces}</span>
              : null}
        </div>
      </div>
    </Link>
  );
}
