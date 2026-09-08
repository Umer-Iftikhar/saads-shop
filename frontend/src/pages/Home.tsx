import { Link } from 'react-router-dom';
import { ProductCard } from '../components/ProductCard';
import { CardSkeleton, ErrorState } from '../components/Feedback';
import { fabricBackground } from '../lib/fabric';
import { useProducts, useShopSettings, useSwatches } from '../hooks/queries';

/**
 * Frame 01 — Home.
 *
 * Wedding sets lead, everything else sits below. That ordering was an explicit
 * ask, and it is why the hero and the first product row are both bridal.
 */
export function Home() {
  const settings = useShopSettings();
  const swatches = useSwatches();

  const featured = useProducts({ category: 'wedding-sets', pageSize: 3 });
  const everyday = useProducts({ category: 'bed-sheets', pageSize: 4 });

  // The hero's three shapes and the swatch strip are drawn from the shop's real
  // palette rather than fixed colours, so adding a cloth changes the home page.
  const palette = swatches.data ?? [];
  const cloth = (index: number) => {
    const swatch = palette[index % Math.max(palette.length, 1)];
    return fabricBackground(swatch?.colorValue, swatch?.weave);
  };

  return (
    <>
      {/* ── hero ─────────────────────────────────────────────────────── */}
      <section
        className="page grid grid-cols-1 items-center gap-8 pb-12 pt-10
                   2xl:grid-cols-[1.05fr_0.95fr] 2xl:gap-12 2xl:pb-[72px] 2xl:pt-16"
      >
        <div>
          <span className="tag tag-accent-2 mb-5 inline-flex">
            Shaadi season · stitched to measure
          </span>

          <h1 className="display-hero mb-5 mt-0 max-w-[12ch]">
            Wedding sets, made in Rawalpindi.
          </h1>

          <p className="mb-7 mt-0 max-w-[46ch] text-[19px]/[1.6] text-pretty text-neutral-700">
            Bridal bedding — sheets, covers and cushions — and full room packages with the
            parde matched to the bistar. Pick your cloth in the shop, or build the set here.
          </p>

          <div className="flex flex-wrap gap-3">
            <Link to="/build-your-set" className="btn btn-primary btn-lg">Build your set</Link>
            <Link to="/wedding-sets" className="btn btn-secondary btn-lg">See wedding sets</Link>
          </div>
        </div>

        {/*  Decorative cloth shapes — the design's stand-in for photography.
            Hidden below the split, where they would cost a full screen of
            scrolling before the first real content.                         */}
        <div className="relative hidden h-[460px] 2xl:block" aria-hidden="true">
          <div className="absolute inset-y-0 bottom-[22%] left-0 right-[20%] rounded-lg shadow-lg" style={{ background: cloth(5) }} />
          <div className="absolute right-0 top-[16%] h-[52%] w-[46%] rounded-b-3xl rounded-t-full shadow-md" style={{ background: cloth(1) }} />
          <div className="absolute bottom-0 left-[12%] h-[148px] w-[148px] rounded-full shadow-md" style={{ background: cloth(4) }} />
          <div className="absolute bottom-[2%] right-[14%] rounded-full bg-bg px-[18px] py-2.5 text-[13px] shadow-sm">
            Custom stitching · 3 days
          </div>
        </div>
      </section>

      {/* ── bridal & jahez ───────────────────────────────────────────── */}
      <section className="page pb-16">
        <div className="mb-4 flex items-end justify-between gap-6">
          <h2 className="display-section m-0">Bridal &amp; jahez sets</h2>
          <Link to="/wedding-sets" className="text-[15px]">All sets →</Link>
        </div>

        <div className="grid grid-cols-1 gap-[22px] md:grid-cols-2 2xl:grid-cols-3">
          {featured.isPending && <CardSkeleton count={3} height={360} />}
          {featured.isError && (
            <div className="col-span-full">
              <ErrorState
                title="Could not load the wedding sets"
                detail="The shop is still here — this is our end."
                onRetry={() => featured.refetch()}
              />
            </div>
          )}
          {featured.data?.items.map(product => (
            <ProductCard key={product.productId} product={product} size="featured" />
          ))}
        </div>
      </section>

      {/* ── set builder invitation ───────────────────────────────────── */}
      <section className="page pb-16">
        <div
          className="grid grid-cols-1 items-center gap-8 rounded-lg bg-neutral-900 px-7 py-8
                     text-neutral-100 2xl:grid-cols-[1.2fr_0.8fr] 2xl:gap-10 2xl:px-[50px] 2xl:py-[46px]"
        >
          <div>
            <h2 className="mb-3 mt-0 text-[clamp(28px,3.4vw,42px)] text-accent-300">
              Match the parde to the bistar.
            </h2>
            <p className="mb-5 mt-0 max-w-[48ch] text-[17px] text-neutral-300">
              Click through the fabric swatches and watch the room change. When it looks right,
              send the combination to the shop and we stitch it.
            </p>
            <Link to="/build-your-set" className="btn btn-primary px-6 py-3">
              Open the set builder
            </Link>
          </div>

          <div className="flex gap-2.5" aria-hidden="true">
            {[0, 5, 1, 4].map(index => (
              <div key={index} className="h-40 flex-1 rounded-full" style={{ background: cloth(index) }} />
            ))}
          </div>
        </div>
      </section>

      {/* ── everyday ─────────────────────────────────────────────────── */}
      <section className="page pb-16">
        <h2 className="display-section mb-4 mt-0">Bistar, parde, chhata</h2>

        <div className="grid grid-cols-1 gap-[18px] md:grid-cols-2 3xl:grid-cols-4">
          {everyday.isPending && <CardSkeleton count={4} height={230} />}
          {everyday.data?.items.map(product => (
            <ProductCard key={product.productId} product={product} size="compact" />
          ))}
        </div>
      </section>

      {/* ── three ways to order ──────────────────────────────────────── */}
      <section className="bg-surface">
        <div className="page py-14">
          <h2 className="display-section mb-8 mt-0">Three ways to order</h2>

          <div className="grid grid-cols-1 gap-[30px] md:grid-cols-2 2xl:grid-cols-3">
            {[
              { n: '1', title: 'Cash on delivery', body: 'We bring the set to your door inside Rawalpindi and you pay the rider.' },
              { n: '2', title: 'WhatsApp or call',  body: 'Send a photo or your measurements. We quote, you confirm, we stitch.' },
              { n: '3', title: 'Reserve, pay in shop', body: 'Hold the cloth online and see it in daylight at Moti Bazaar before paying.' },
            ].map(way => (
              <div key={way.n} className="flex items-start gap-4">
                <div
                  aria-hidden="true"
                  className="grid h-[58px] w-[58px] flex-none place-items-center rounded-full
                             bg-accent font-heading text-2xl text-bg"
                >
                  {way.n}
                </div>
                <div>
                  <h3 className="mb-1 text-[21px]">{way.title}</h3>
                  <p className="m-0 text-sm text-neutral-700">{way.body}</p>
                </div>
              </div>
            ))}
          </div>
        </div>
      </section>

      <ShopFooter settings={settings.data} />
    </>
  );
}

export function ShopFooter({ settings }: { settings?: { shopName: string; addressLine: string; city: string; openingHours?: string | null; whatsAppNumber: string } }) {
  return (
    <footer className="page py-11">
      <div className="grid grid-cols-1 gap-[30px] text-sm text-neutral-700 md:grid-cols-2 2xl:grid-cols-3">
        <div>
          <div className="mb-2 font-heading text-[21px] text-text">
            {settings?.shopName ?? "Saad's Shop"}
          </div>
          <div>{settings?.addressLine ?? 'Shop 14, Moti Bazaar'}</div>
          <div>{settings?.city ? `${settings.city}` : 'Raja Bazaar, Rawalpindi'}</div>
        </div>

        <div>
          <div className="mb-2 font-bold text-text">Open</div>
          {(settings?.openingHours ?? 'Mon–Sat · 10am – 9pm · Friday break 1pm – 2:30pm')
            .split('·')
            .map((part, i) => <div key={i}>{part.trim()}</div>)}
        </div>

        <div>
          <div className="mb-2 font-bold text-text">Order on WhatsApp</div>
          <div className="font-heading text-[21px] text-text">
            {settings?.whatsAppNumber ?? '0300 000 0000'}
          </div>
          <div className="mt-1">Send a photo, get a quote.</div>
        </div>
      </div>
    </footer>
  );
}
