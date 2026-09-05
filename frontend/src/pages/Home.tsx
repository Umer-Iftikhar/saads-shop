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
      <section className="page page-pad hero">
        <div>
          <span className="tag tag-accent-2" style={{ marginBottom: 20, display: 'inline-flex' }}>
            Shaadi season · stitched to measure
          </span>

          <h1 className="display-hero" style={{ margin: '0 0 22px', maxWidth: '12ch' }}>
            Wedding sets, made in Rawalpindi.
          </h1>

          <p style={{
            fontSize: 19, lineHeight: 1.6, maxWidth: '46ch',
            color: 'var(--color-neutral-700)', margin: '0 0 30px', textWrap: 'pretty',
          }}>
            Bridal bedding — sheets, covers and cushions — and full room packages with the
            parde matched to the bistar. Pick your cloth in the shop, or build the set here.
          </p>

          <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
            <Link to="/build-your-set" className="btn btn-primary btn-lg">Build your set</Link>
            <Link to="/wedding-sets" className="btn btn-secondary btn-lg">See wedding sets</Link>
          </div>
        </div>

        {/* Decorative cloth shapes — the design's stand-in for photography. */}
        <div className="hero-art" aria-hidden="true">
          <div style={{ position: 'absolute', inset: '0 20% 22% 0', borderRadius: 'var(--radius-lg)', background: cloth(5), boxShadow: 'var(--shadow-lg)' }} />
          <div style={{ position: 'absolute', right: 0, top: '16%', width: '46%', height: '52%', borderRadius: '999px 999px 24px 24px', background: cloth(1), boxShadow: 'var(--shadow-md)' }} />
          <div style={{ position: 'absolute', left: '12%', bottom: 0, width: 148, height: 148, borderRadius: '50%', background: cloth(4), boxShadow: 'var(--shadow-md)' }} />
          <div style={{
            position: 'absolute', right: '14%', bottom: '2%', background: 'var(--color-bg)',
            borderRadius: 999, padding: '10px 18px', fontSize: 13, boxShadow: 'var(--shadow-sm)',
          }}>
            Custom stitching · 3 days
          </div>
        </div>
      </section>

      {/* ── bridal & jahez ───────────────────────────────────────────── */}
      <section className="page page-pad" style={{ paddingBottom: 64 }}>
        <div style={{ display: 'flex', alignItems: 'end', justifyContent: 'space-between', gap: 24, marginBottom: 18 }}>
          <h2 className="display-section" style={{ margin: 0 }}>Bridal &amp; jahez sets</h2>
          <Link to="/wedding-sets" style={{ fontSize: 15 }}>All sets →</Link>
        </div>

        <div className="grid-3">
          {featured.isPending && <CardSkeleton count={3} height={360} />}
          {featured.isError && (
            <div style={{ gridColumn: '1 / -1' }}>
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
      <section className="page page-pad" style={{ paddingBottom: 64 }}>
        <div className="builder-promo">
          <div>
            <h2 style={{ fontSize: 'clamp(28px, 3.4vw, 42px)', margin: '0 0 12px', color: 'var(--color-accent-300)' }}>
              Match the parde to the bistar.
            </h2>
            <p style={{ margin: '0 0 20px', fontSize: 17, color: 'var(--color-neutral-300)', maxWidth: '48ch' }}>
              Click through the fabric swatches and watch the room change. When it looks right,
              send the combination to the shop and we stitch it.
            </p>
            <Link to="/build-your-set" className="btn btn-primary" style={{ padding: '12px 24px' }}>
              Open the set builder
            </Link>
          </div>

          <div style={{ display: 'flex', gap: 10 }} aria-hidden="true">
            {[0, 5, 1, 4].map(index => (
              <div key={index} style={{ flex: 1, height: 160, borderRadius: 999, background: cloth(index) }} />
            ))}
          </div>
        </div>
      </section>

      {/* ── everyday ─────────────────────────────────────────────────── */}
      <section className="page page-pad" style={{ paddingBottom: 64 }}>
        <h2 className="display-section" style={{ margin: '0 0 18px' }}>Bistar, parde, chhata</h2>

        <div className="grid-4">
          {everyday.isPending && <CardSkeleton count={4} height={230} />}
          {everyday.data?.items.map(product => (
            <ProductCard key={product.productId} product={product} size="compact" />
          ))}
        </div>
      </section>

      {/* ── three ways to order ──────────────────────────────────────── */}
      <section style={{ background: 'var(--color-surface)' }}>
        <div className="page page-pad" style={{ padding: '56px 40px' }}>
          <h2 className="display-section" style={{ margin: '0 0 34px' }}>Three ways to order</h2>

          <div className="grid-3" style={{ gap: 30 }}>
            {[
              { n: '1', title: 'Cash on delivery', body: 'We bring the set to your door inside Rawalpindi and you pay the rider.' },
              { n: '2', title: 'WhatsApp or call',  body: 'Send a photo or your measurements. We quote, you confirm, we stitch.' },
              { n: '3', title: 'Reserve, pay in shop', body: 'Hold the cloth online and see it in daylight at Moti Bazaar before paying.' },
            ].map(way => (
              <div key={way.n} style={{ display: 'flex', gap: 18, alignItems: 'start' }}>
                <div
                  aria-hidden="true"
                  style={{
                    flex: 'none', width: 58, height: 58, borderRadius: '50%',
                    background: 'var(--color-accent)', color: 'var(--color-bg)',
                    display: 'grid', placeItems: 'center',
                    fontFamily: 'var(--font-heading)', fontSize: 24,
                  }}
                >
                  {way.n}
                </div>
                <div>
                  <h3 style={{ fontSize: 21, marginBottom: 5 }}>{way.title}</h3>
                  <p style={{ margin: 0, fontSize: 14, color: 'var(--color-neutral-700)' }}>{way.body}</p>
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
    <footer className="page page-pad" style={{ padding: '44px 40px' }}>
      <div className="grid-3" style={{ gap: 30, fontSize: 14, color: 'var(--color-neutral-700)' }}>
        <div>
          <div style={{ fontFamily: 'var(--font-heading)', fontSize: 21, color: 'var(--color-text)', marginBottom: 8 }}>
            {settings?.shopName ?? "Saad's Shop"}
          </div>
          <div>{settings?.addressLine ?? 'Shop 14, Moti Bazaar'}</div>
          <div>{settings?.city ? `${settings.city}` : 'Raja Bazaar, Rawalpindi'}</div>
        </div>

        <div>
          <div style={{ fontWeight: 700, color: 'var(--color-text)', marginBottom: 8 }}>Open</div>
          {(settings?.openingHours ?? 'Mon–Sat · 10am – 9pm · Friday break 1pm – 2:30pm')
            .split('·')
            .map((part, i) => <div key={i}>{part.trim()}</div>)}
        </div>

        <div>
          <div style={{ fontWeight: 700, color: 'var(--color-text)', marginBottom: 8 }}>Order on WhatsApp</div>
          <div style={{ fontSize: 21, fontFamily: 'var(--font-heading)', color: 'var(--color-text)' }}>
            {settings?.whatsAppNumber ?? '0300 000 0000'}
          </div>
          <div style={{ marginTop: 5 }}>Send a photo, get a quote.</div>
        </div>
      </div>
    </footer>
  );
}
