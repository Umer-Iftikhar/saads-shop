import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { SwatchGroup } from '../components/Swatch';
import { ProductCard } from '../components/ProductCard';
import { ErrorState, Loading } from '../components/Feedback';
import { fabricBackground } from '../lib/fabric';
import { formatPkr, whatsAppLink } from '../lib/format';
import { useBedSizes, useProduct, useShopSettings } from '../hooks/queries';
import { useCart } from '../state/cart';

/** Categories cut to a bed, so only these show the size picker. */
const SIZED_CATEGORIES = new Set(['wedding-sets', 'bed-sheets']);

/**
 * Frame 03 — product detail, with the clickable fabric swatches that were the
 * design's centrepiece.
 */
export function Product() {
  const { slug } = useParams<{ slug: string }>();
  const cart = useCart();

  const product  = useProduct(slug);
  const bedSizes = useBedSizes();
  const settings = useShopSettings();

  // null means "not chosen yet", which resolves to the product's own cloth
  // below. Deriving it during render rather than seeding state in an effect
  // avoids a second render on every product, and means navigating between
  // products cannot briefly show the previous product's swatch.
  const [pickedSwatchId, setPickedSwatchId] = useState<number | null>(null);
  const [bedSize, setBedSize] = useState<string>('Double');
  const [added, setAdded]     = useState(false);

  if (product.isPending) return <Loading label="Loading the product…" />;

  if (product.isError || !product.data) {
    return (
      <main id="main" className="page page-pad" style={{ paddingBlock: 40 }}>
        <ErrorState
          title="We could not find that product"
          detail="It may have sold out and been taken down. The wedding sets are still here."
          onRetry={() => product.refetch()}
        />
        <div style={{ textAlign: 'center' }}>
          <Link to="/wedding-sets" className="btn btn-primary">See wedding sets</Link>
        </div>
      </main>
    );
  }

  const item = product.data;
  const swatchId = pickedSwatchId ?? item.defaultSwatchId ?? item.swatches[0]?.swatchId ?? null;
  const chosen = item.swatches.find(s => s.swatchId === swatchId) ?? item.swatches[0];
  const takesBedSize = SIZED_CATEGORIES.has(item.categorySlug);

  function addToCart() {
    cart.add({
      productId: item.productId,
      name: item.name,
      price: item.price,
      slug: item.slug,
      quantity: 1,
      swatchId: chosen?.swatchId ?? null,
      swatchName: chosen?.name ?? null,
      swatchColorValue: chosen?.colorValue ?? null,
      swatchWeave: chosen?.weave ?? null,
      bedSize: takesBedSize ? bedSize : null,
      pieces: item.pieces ?? null,
    });

    setAdded(true);
    window.setTimeout(() => setAdded(false), 2500);
  }

  const whatsAppMessage =
    `Assalam o alaikum! I would like to order the ${item.name}` +
    `${chosen ? ` in ${chosen.name}` : ''}${takesBedSize ? ` (${bedSize})` : ''}.`;

  return (
    <main id="main" className="page page-pad" style={{ paddingBlock: '30px 64px' }}>
      <Link to={`/${item.categorySlug}`} style={{ fontSize: 14, display: 'inline-block', marginBottom: 20 }}>
        ← Back to {item.categoryName.toLowerCase()}
      </Link>

      <div className="product-split">
        <div>
          <div
            className="washed"
            style={{
              height: 470,
              borderRadius: 'var(--radius-lg)',
              background: fabricBackground(chosen?.colorValue, chosen?.weave),
              boxShadow: 'var(--shadow-lg)',
            }}
            role="img"
            aria-label={`${item.name} in ${chosen?.name ?? 'the shop cloth'}`}
          />

          {item.swatches.length > 0 && (
            <div style={{ marginTop: 16 }}>
              <SwatchGroup
                label="Choose the cloth"
                swatches={item.swatches}
                selectedId={swatchId}
                onSelect={setPickedSwatchId}
              />
              <p style={{ fontSize: 13, color: 'var(--color-neutral-600)', marginTop: 12 }}>
                Fabric: {chosen?.name} · tap a swatch to change the cloth
              </p>
            </div>
          )}
        </div>

        <div>
          {item.kicker && <span className="tag tag-accent">{item.kicker}</span>}

          <h1 style={{ fontSize: 'clamp(32px, 4vw, 50px)', lineHeight: 1, margin: '16px 0 12px' }}>
            {item.name}
          </h1>

          {item.longDescription && (
            <p style={{ fontSize: 17, color: 'var(--color-neutral-700)', margin: '0 0 20px', textWrap: 'pretty' }}>
              {item.longDescription}
            </p>
          )}

          <div style={{ fontFamily: 'var(--font-heading)', fontSize: 36, marginBottom: 24 }}>
            {formatPkr(item.price)}
          </div>

          {takesBedSize && bedSizes.data && (
            <fieldset style={{ border: 0, padding: 0, margin: '0 0 24px' }}>
              <legend style={{ fontSize: 12, color: 'var(--color-neutral-600)', marginBottom: 8, padding: 0 }}>
                BED SIZE
              </legend>
              <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
                {bedSizes.data.map(size => (
                  <button
                    key={size.bedSizeCode}
                    type="button"
                    className="chip"
                    onClick={() => setBedSize(size.bedSizeCode)}
                    aria-pressed={bedSize === size.bedSizeCode}
                  >
                    {size.name}
                    {size.priceAdjustment !== 0 && (
                      <span className="chip-note">
                        {size.priceAdjustment > 0 ? '+' : '−'}{formatPkr(Math.abs(size.priceAdjustment))}
                      </span>
                    )}
                  </button>
                ))}
              </div>
            </fieldset>
          )}

          <div style={{ display: 'flex', flexDirection: 'column', gap: 10, maxWidth: 340 }}>
            <button
              type="button"
              className="btn btn-primary btn-block btn-lg"
              onClick={addToCart}
              disabled={!item.inStock}
            >
              {item.inStock ? 'Add to cart' : 'Out of stock'}
            </button>

            <a
              className="btn btn-secondary btn-block btn-lg"
              href={whatsAppLink(settings.data?.whatsAppNumber ?? '03000000000', whatsAppMessage)}
              target="_blank"
              rel="noopener noreferrer"
            >
              Order on WhatsApp
            </a>
          </div>

          {/*  Announced politely so a screen reader hears the confirmation
              without the focus being yanked away from the button.           */}
          <div aria-live="polite" style={{ minHeight: 24, marginTop: 10 }}>
            {added && (
              <span className="tag tag-accent-2">
                Added to your cart · <Link to="/cart" style={{ color: 'inherit' }}>view cart</Link>
              </span>
            )}
          </div>

          <div style={{ marginTop: 28, padding: 22, borderRadius: 'var(--radius-lg)', background: 'var(--color-surface)' }}>
            <h2 style={{ fontSize: 19, marginBottom: 8 }}>Made to measure</h2>
            <p style={{ margin: 0, fontSize: 14, color: 'var(--color-neutral-700)' }}>
              Bring your bed or window measurements to the shop, or send them on WhatsApp.
              Stitching takes {item.stitchingDays === 1 ? 'one working day' : `${item.stitchingDays} working days`};
              wedding orders get priority in shaadi season.
            </p>
          </div>
        </div>
      </div>

      {item.related.length > 0 && (
        <section style={{ marginTop: 64 }}>
          <h2 className="display-section" style={{ marginBottom: 18 }}>More {item.categoryName.toLowerCase()}</h2>
          <div className="grid-3">
            {item.related.map(related => (
              <ProductCard key={related.productId} product={related} size="listing" />
            ))}
          </div>
        </section>
      )}
    </main>
  );
}
