import { useEffect, useMemo, useState } from 'react';
import { useMutation } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { SwatchGroup } from '../components/Swatch';
import { Loading, ErrorState } from '../components/Feedback';
import { api, ApiError } from '../lib/api';
import { fabricBackground } from '../lib/fabric';
import { formatPkr } from '../lib/format';
import { useBedSizes, useProducts, useSwatches } from '../hooks/queries';
import { useCart } from '../state/cart';
import type { SetBuilderQuote, SetBuilderQuoteRequest } from '../types/api';

/**
 * Frame 04 — Build your set.
 *
 * Pick a cloth for the bistar, the parde and the cushions; the room redraws as
 * you go. The total is priced by the server, not here: the browser knows what
 * was chosen, the database knows what it costs.
 */
/**  A stable reference, so `palette` does not become a new array every render
     and invalidate every memo that depends on it.                            */
const NO_SWATCHES: { swatchId: number; name: string; colorValue: string; weave?: string | null }[] = [];

export function SetBuilder() {
  const cart = useCart();
  const navigate = useNavigate();

  const swatches = useSwatches();
  const bedSizes = useBedSizes();

  // One product per slot — the cheapest of each kind is a sensible default and
  // keeps the builder about the cloth rather than the product.
  const sheets   = useProducts({ category: 'bed-sheets', pageSize: 1, sortBy: 'PriceAsc' });
  const curtains = useProducts({ category: 'curtains',   pageSize: 1, sortBy: 'PriceAsc' });
  const cushions = useProducts({ category: 'cushions',   pageSize: 1, sortBy: 'PriceAsc' });

  // null means "not chosen yet". The defaults are derived below rather than
  // written into state by an effect — seeding state from data causes a second
  // render on every load, and briefly shows the wrong cloth while it settles.
  const [pickedSheet,   setPickedSheet]   = useState<number | null>(null);
  const [pickedCurtain, setPickedCurtain] = useState<number | null>(null);
  const [pickedCushion, setPickedCushion] = useState<number | null>(null);
  const [bedSize, setBedSize] = useState('Double');
  const [quote, setQuote] = useState<SetBuilderQuote | null>(null);
  const [quoteError, setQuoteError] = useState<string | null>(null);

  const palette = swatches.data ?? NO_SWATCHES;

  // Terracotta, Clay and Gold — the combination the design opens on, falling
  // back through the palette if the shop has fewer cloths.
  const sheetSwatch   = pickedSheet   ?? palette[0]?.swatchId ?? null;
  const curtainSwatch = pickedCurtain ?? palette[3]?.swatchId ?? palette[1]?.swatchId ?? null;
  const cushionSwatch = pickedCushion ?? palette[4]?.swatchId ?? palette[2]?.swatchId ?? null;

  const sheet   = sheets.data?.items[0];
  const curtain = curtains.data?.items[0];
  const cushion = cushions.data?.items[0];

  const quoteMutation = useMutation({
    mutationFn: (request: SetBuilderQuoteRequest) =>
      api.post<SetBuilderQuote>('/set-builder/quote', request),
    onSuccess: result => { setQuote(result); setQuoteError(null); },
    onError: (error: ApiError) => {
      setQuote(null);
      setQuoteError(error.message);
    },
  });

  // `mutate` is stable across renders; the mutation object is not, so depending
  // on the object would re-price on every render.
  const { mutate: requestQuote } = quoteMutation;

  const sheetId   = sheet?.productId;
  const curtainId = curtain?.productId;
  const cushionId = cushion?.productId;

  // Re-price whenever the combination changes. Only the server knows what a set
  // costs, so the total on screen is always its answer, never local arithmetic.
  useEffect(() => {
    if (!sheetId || !curtainId || !cushionId) return;

    requestQuote({
      sheetProductId:   sheetId,
      curtainProductId: curtainId,
      cushionProductId: cushionId,
      bedSize,
    });
  }, [sheetId, curtainId, cushionId, bedSize, requestQuote]);

  const cloth = useMemo(() => {
    const find = (id: number | null) => palette.find(s => s.swatchId === id);
    return {
      sheet:   find(sheetSwatch),
      curtain: find(curtainSwatch),
      cushion: find(cushionSwatch),
    };
  }, [palette, sheetSwatch, curtainSwatch, cushionSwatch]);

  function addSetToCart() {
    if (!sheet || !curtain || !cushion) return;

    const pieces: [typeof sheet, number | null, string | null][] = [
      [sheet,   sheetSwatch,   bedSize],
      [curtain, curtainSwatch, null],
      [cushion, cushionSwatch, null],
    ];

    for (const [product, swatchId, size] of pieces) {
      const chosen = palette.find(s => s.swatchId === swatchId);

      cart.add({
        productId: product.productId,
        name: product.name,
        price: product.price,
        slug: product.slug,
        quantity: 1,
        swatchId,
        swatchName: chosen?.name ?? null,
        swatchColorValue: chosen?.colorValue ?? null,
        swatchWeave: chosen?.weave ?? null,
        bedSize: size,
        pieces: product.pieces ?? null,
      });
    }

    navigate('/cart');
  }

  if (swatches.isPending || sheets.isPending || curtains.isPending || cushions.isPending) {
    return <Loading label="Setting out the cloth…" />;
  }

  if (!sheet || !curtain || !cushion) {
    return (
      <main id="main" className="page page-pad" style={{ paddingBlock: 40 }}>
        <ErrorState
          title="The set builder is short of cloth"
          detail="It needs a bed sheet, a curtain and a cushion set in the catalogue. Ask the shop to add them."
        />
      </main>
    );
  }

  return (
    <main id="main" className="page page-pad" style={{ paddingBlock: '34px 64px' }}>
      <h1 className="display-page" style={{ margin: '0 0 8px' }}>Build your set</h1>

      <p style={{ margin: '0 0 26px', color: 'var(--color-neutral-700)', fontSize: 16, maxWidth: '62ch' }}>
        Pick the cloth for the bistar, the parde and the cushions. The room updates as you
        go — then send the combination to the shop.
      </p>

      <div className="builder-split">
        {/* ── the three pickers ────────────────────────────────────── */}
        <div style={{ display: 'flex', flexDirection: 'column', gap: 22 }}>
          <SlotPicker
            title="Bistar — the sheet"
            picked={cloth.sheet?.name}
            swatches={palette}
            selectedId={sheetSwatch}
            onSelect={setPickedSheet}
          />
          <SlotPicker
            title="Parde — the curtains"
            picked={cloth.curtain?.name}
            swatches={palette}
            selectedId={curtainSwatch}
            onSelect={setPickedCurtain}
          />
          <SlotPicker
            title="Cushions"
            picked={cloth.cushion?.name}
            swatches={palette}
            selectedId={cushionSwatch}
            onSelect={setPickedCushion}
          />
        </div>

        {/* ── the room ─────────────────────────────────────────────── */}
        <div
          role="img"
          aria-label={
            `A room with ${cloth.sheet?.name ?? 'plain'} bedding, ` +
            `${cloth.curtain?.name ?? 'plain'} curtains and ` +
            `${cloth.cushion?.name ?? 'plain'} cushions, on a ${bedSize.toLowerCase()} bed.`
          }
          style={{
            position: 'relative', height: 520, borderRadius: 'var(--radius-lg)',
            overflow: 'hidden', background: 'var(--color-neutral-200)', boxShadow: 'var(--shadow-md)',
          }}
        >
          <div style={{ position: 'absolute', left: 0, right: 0, bottom: 0, height: '22%', background: 'var(--color-neutral-300)' }} />
          {/* window */}
          <div style={{ position: 'absolute', left: '7%', top: '9%', width: '38%', height: '46%', borderRadius: '999px 999px 20px 20px', background: 'linear-gradient(#fffaf0, #ffe7d4)', boxShadow: 'inset 0 0 0 6px var(--color-neutral-100)' }} />
          {/* curtain panels */}
          <div style={{ position: 'absolute', left: '3%',  top: '6%', width: '15%', height: '56%', borderRadius: '999px 999px 16px 16px', background: fabricBackground(cloth.curtain?.colorValue, cloth.curtain?.weave), boxShadow: 'var(--shadow-md)' }} />
          <div style={{ position: 'absolute', left: '34%', top: '6%', width: '15%', height: '56%', borderRadius: '999px 999px 16px 16px', background: fabricBackground(cloth.curtain?.colorValue, cloth.curtain?.weave), boxShadow: 'var(--shadow-md)' }} />
          {/* bed */}
          <div style={{ position: 'absolute', right: '5%', bottom: '14%', width: '58%', height: '34%', borderRadius: 26, background: fabricBackground(cloth.sheet?.colorValue, cloth.sheet?.weave), boxShadow: 'var(--shadow-lg)' }} />
          <div style={{ position: 'absolute', right: '60%', bottom: '14%', width: '8%', height: '44%', borderRadius: '999px 999px 0 0', background: 'var(--color-neutral-400)' }} />
          {/* cushions */}
          <div style={{ position: 'absolute', right: '44%', bottom: '36%', width: 72, height: 72, borderRadius: 24, background: fabricBackground(cloth.cushion?.colorValue, cloth.cushion?.weave), boxShadow: 'var(--shadow-md)' }} />
          <div style={{ position: 'absolute', right: '30%', bottom: '36%', width: 58, height: 58, borderRadius: 20, background: fabricBackground(cloth.cushion?.colorValue, cloth.cushion?.weave), boxShadow: 'var(--shadow-sm)' }} />

          <div style={{ position: 'absolute', left: 22, bottom: 20, background: 'var(--color-bg)', borderRadius: 999, padding: '8px 16px', fontSize: 12, boxShadow: 'var(--shadow-sm)' }}>
            Preview · {bedSize} bed
          </div>
        </div>

        {/* ── the summary ──────────────────────────────────────────── */}
        <div className="card elev-md" style={{ padding: 24, gap: 0 }}>
          <h2 style={{ fontSize: 23, marginBottom: 14 }}>Your set</h2>

          {quote?.lines.map(line => (
            <div
              key={line.slot}
              style={{
                display: 'flex', justifyContent: 'space-between', gap: 12, fontSize: 14,
                padding: '9px 0', borderBottom: '1px solid var(--color-divider)',
              }}
            >
              <span>
                {line.slot} · {line.slot === 'Bistar' ? cloth.sheet?.name : line.slot === 'Parde' ? cloth.curtain?.name : cloth.cushion?.name}
                {!line.inStock && <span className="tag tag-neutral" style={{ marginLeft: 6 }}>Out of stock</span>}
              </span>
              <span style={{ fontWeight: 700, whiteSpace: 'nowrap' }}>{formatPkr(line.unitPrice)}</span>
            </div>
          ))}

          {quoteMutation.isPending && !quote && (
            <p style={{ fontSize: 14, color: 'var(--color-neutral-600)' }}>Pricing your set…</p>
          )}

          {quoteError && <div className="field-error" role="alert">{quoteError}</div>}

          <fieldset style={{ border: 0, padding: 0, margin: '16px 0 0' }}>
            <legend style={{ fontSize: 12, color: 'var(--color-neutral-600)', marginBottom: 8, padding: 0 }}>
              BED SIZE
            </legend>
            <div style={{ display: 'flex', gap: 7, flexWrap: 'wrap', marginBottom: 16 }}>
              {bedSizes.data?.map(size => (
                <button
                  key={size.bedSizeCode}
                  type="button"
                  className="chip"
                  onClick={() => setBedSize(size.bedSizeCode)}
                  aria-pressed={bedSize === size.bedSizeCode}
                >
                  {size.name}
                </button>
              ))}
            </div>
          </fieldset>

          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', marginBottom: 16 }}>
            <span style={{ fontSize: 13, color: 'var(--color-neutral-600)' }}>Total</span>
            <span style={{ fontFamily: 'var(--font-heading)', fontSize: 28 }} aria-live="polite">
              {quote ? formatPkr(quote.total) : '—'}
            </span>
          </div>

          <button
            type="button"
            className="btn btn-primary btn-block"
            style={{ padding: 13 }}
            onClick={addSetToCart}
            disabled={!quote}
          >
            Add set to cart
          </button>
        </div>
      </div>
    </main>
  );
}

function SlotPicker({ title, picked, swatches, selectedId, onSelect }: {
  title: string;
  picked?: string;
  swatches: { swatchId: number; name: string; colorValue: string; weave?: string | null }[];
  selectedId: number | null;
  onSelect: (id: number) => void;
}) {
  return (
    <div>
      <h2 style={{ fontSize: 19, marginBottom: 3 }}>{title}</h2>
      <div style={{ fontSize: 12, color: 'var(--color-neutral-600)', marginBottom: 10 }}>{picked ?? '—'}</div>
      <SwatchGroup label={title} swatches={swatches} selectedId={selectedId} onSelect={onSelect} size={62} />
    </div>
  );
}
