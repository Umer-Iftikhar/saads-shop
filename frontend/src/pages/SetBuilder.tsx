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
      <main id="main" className="page py-10">
        <ErrorState
          title="The set builder is short of cloth"
          detail="It needs a bed sheet, a curtain and a cushion set in the catalogue. Ask the shop to add them."
        />
      </main>
    );
  }

  return (
    <main id="main" className="page pb-16 pt-[34px]">
      <h1 className="display-page mb-2 mt-0">Build your set</h1>

      <p className="mb-6 mt-0 max-w-[62ch] text-base text-neutral-700">
        Pick the cloth for the bistar, the parde and the cushions. The room updates as you
        go — then send the combination to the shop.
      </p>

      {/*  Three columns at the design's width; the summary drops below the
          room first, then the pickers stack too on a phone.                */}
      <div
        className="grid grid-cols-1 items-start gap-7
                   md:grid-cols-[220px_1fr] 3xl:grid-cols-[250px_1fr_290px]
                   [&>*:last-child]:md:col-span-full [&>*:last-child]:3xl:col-span-1"
      >
        {/* ── the three pickers ────────────────────────────────────── */}
        <div className="flex flex-col gap-5">
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
          className="relative h-[520px] overflow-hidden rounded-lg bg-neutral-200 shadow-md"
        >
          {/* floor */}
          <div className="absolute inset-x-0 bottom-0 h-[22%] bg-neutral-300" />
          {/* window */}
          <div className="absolute left-[7%] top-[9%] h-[46%] w-[38%] rounded-b-[20px] rounded-t-full
                          bg-[linear-gradient(#fffaf0,#ffe7d4)] shadow-[inset_0_0_0_6px_var(--color-neutral-100)]" />
          {/* curtain panels */}
          <div
            className="absolute left-[3%] top-[6%] h-[56%] w-[15%] rounded-b-2xl rounded-t-full shadow-md"
            style={{ background: fabricBackground(cloth.curtain?.colorValue, cloth.curtain?.weave) }}
          />
          <div
            className="absolute left-[34%] top-[6%] h-[56%] w-[15%] rounded-b-2xl rounded-t-full shadow-md"
            style={{ background: fabricBackground(cloth.curtain?.colorValue, cloth.curtain?.weave) }}
          />
          {/* bed */}
          <div
            className="absolute bottom-[14%] right-[5%] h-[34%] w-[58%] rounded-[26px] shadow-lg"
            style={{ background: fabricBackground(cloth.sheet?.colorValue, cloth.sheet?.weave) }}
          />
          <div className="absolute bottom-[14%] right-[60%] h-[44%] w-[8%] rounded-t-full bg-neutral-400" />
          {/* cushions */}
          <div
            className="absolute bottom-[36%] right-[44%] h-[72px] w-[72px] rounded-3xl shadow-md"
            style={{ background: fabricBackground(cloth.cushion?.colorValue, cloth.cushion?.weave) }}
          />
          <div
            className="absolute bottom-[36%] right-[30%] h-[58px] w-[58px] rounded-[20px] shadow-sm"
            style={{ background: fabricBackground(cloth.cushion?.colorValue, cloth.cushion?.weave) }}
          />

          <div className="absolute bottom-5 left-5 rounded-full bg-bg px-4 py-2 text-xs shadow-sm">
            Preview · {bedSize} bed
          </div>
        </div>

        {/* ── the summary ──────────────────────────────────────────── */}
        <div className="card gap-0 p-6 shadow-md">
          <h2 className="mb-3.5 text-[23px]">Your set</h2>

          {quote?.lines.map(line => (
            <div key={line.slot} className="flex justify-between gap-3 border-b border-divider py-2 text-sm">
              <span>
                {line.slot} · {line.slot === 'Bistar' ? cloth.sheet?.name : line.slot === 'Parde' ? cloth.curtain?.name : cloth.cushion?.name}
                {!line.inStock && <span className="tag tag-neutral ml-1.5">Out of stock</span>}
              </span>
              <span className="whitespace-nowrap font-bold">{formatPkr(line.unitPrice)}</span>
            </div>
          ))}

          {quoteMutation.isPending && !quote && (
            <p className="text-sm text-neutral-600">Pricing your set…</p>
          )}

          {quoteError && <div className="field-error" role="alert">{quoteError}</div>}

          <fieldset className="mt-4 border-0 p-0">
            <legend className="mb-2 p-0 text-xs text-neutral-600">
              BED SIZE
            </legend>
            <div className="mb-4 flex flex-wrap gap-1.5">
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

          <div className="mb-4 flex items-baseline justify-between">
            <span className="text-[13px] text-neutral-600">Total</span>
            <span className="font-heading text-[28px]" aria-live="polite">
              {quote ? formatPkr(quote.total) : '—'}
            </span>
          </div>

          <button
            type="button"
            className="btn btn-primary btn-block p-3"
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
      <h2 className="mb-1 text-[19px]">{title}</h2>
      <div className="mb-2.5 text-xs text-neutral-600">{picked ?? '—'}</div>
      <SwatchGroup label={title} swatches={swatches} selectedId={selectedId} onSelect={onSelect} size={62} />
    </div>
  );
}
