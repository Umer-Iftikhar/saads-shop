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
      <main id="main" className="page py-10">
        <ErrorState
          title="We could not find that product"
          detail="It may have sold out and been taken down. The wedding sets are still here."
          onRetry={() => product.refetch()}
        />
        <div className="text-center">
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
    <main id="main" className="page pb-16 pt-[30px]">
      <Link to={`/${item.categorySlug}`} className="mb-5 inline-block text-sm">
        ← Back to {item.categoryName.toLowerCase()}
      </Link>

      <div className="grid grid-cols-1 items-start gap-12 2xl:grid-cols-[1fr_0.85fr]">
        <div>
          <div
            className="washed h-[470px] rounded-lg shadow-lg"
            style={{ background: fabricBackground(chosen?.colorValue, chosen?.weave) }}
            role="img"
            aria-label={`${item.name} in ${chosen?.name ?? 'the shop cloth'}`}
          />

          {item.swatches.length > 0 && (
            <div className="mt-4">
              <SwatchGroup
                label="Choose the cloth"
                swatches={item.swatches}
                selectedId={swatchId}
                onSelect={setPickedSwatchId}
              />
              <p className="mt-3 text-[13px] text-neutral-600">
                Fabric: {chosen?.name} · tap a swatch to change the cloth
              </p>
            </div>
          )}
        </div>

        <div>
          {item.kicker && <span className="tag tag-accent">{item.kicker}</span>}

          <h1 className="mb-3 mt-4 text-[clamp(32px,4vw,50px)]/none">
            {item.name}
          </h1>

          {item.longDescription && (
            <p className="mb-5 mt-0 text-[17px] text-pretty text-neutral-700">
              {item.longDescription}
            </p>
          )}

          <div className="mb-6 font-heading text-4xl">
            {formatPkr(item.price)}
          </div>

          {takesBedSize && bedSizes.data && (
            <fieldset className="mb-6 border-0 p-0">
              <legend className="mb-2 p-0 text-xs text-neutral-600">
                BED SIZE
              </legend>
              <div className="flex flex-wrap gap-2">
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
                      <span className="text-xs opacity-75">
                        {size.priceAdjustment > 0 ? '+' : '−'}{formatPkr(Math.abs(size.priceAdjustment))}
                      </span>
                    )}
                  </button>
                ))}
              </div>
            </fieldset>
          )}

          <div className="flex max-w-[340px] flex-col gap-2.5">
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
          <div aria-live="polite" className="mt-2.5 min-h-6">
            {added && (
              <span className="tag tag-accent-2">
                Added to your cart · <Link to="/cart" className="text-inherit">view cart</Link>
              </span>
            )}
          </div>

          <div className="mt-7 rounded-lg bg-surface p-5">
            <h2 className="mb-2 text-[19px]">Made to measure</h2>
            <p className="m-0 text-sm text-neutral-700">
              Bring your bed or window measurements to the shop, or send them on WhatsApp.
              Stitching takes {item.stitchingDays === 1 ? 'one working day' : `${item.stitchingDays} working days`};
              wedding orders get priority in shaadi season.
            </p>
          </div>
        </div>
      </div>

      {item.related.length > 0 && (
        <section className="mt-16">
          <h2 className="display-section mb-4">More {item.categoryName.toLowerCase()}</h2>
          <div className="grid grid-cols-1 gap-[22px] md:grid-cols-2 2xl:grid-cols-3">
            {item.related.map(related => (
              <ProductCard key={related.productId} product={related} size="listing" />
            ))}
          </div>
        </section>
      )}
    </main>
  );
}
