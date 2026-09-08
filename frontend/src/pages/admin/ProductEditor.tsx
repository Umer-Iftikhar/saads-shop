import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { Field } from '../../components/Field';
import { PhotoField } from '../../components/admin/PhotoField';
import { Swatch } from '../../components/Swatch';
import { ErrorState, Loading } from '../../components/Feedback';
import { ApiError } from '../../lib/api';
import { fabricBackground } from '../../lib/fabric';
import { formatPkr } from '../../lib/format';
import { fromApiErrors } from '../../lib/validation';
import type { FieldErrors } from '../../lib/validation';
import { useCategories, useSwatches } from '../../hooks/queries';
import { useAdminProducts, useSaveProduct } from '../../hooks/adminQueries';

/**
 * Frame 11 — the product editor, with the live shop preview beside it.
 *
 * Photography is deliberately absent: the shop has none yet, so a product is
 * defined by its cloths and the preview draws them. An upload field is the one
 * thing that changes when real photos arrive.
 */
export function ProductEditor() {
  const { productId: param } = useParams<{ productId?: string }>();
  const navigate = useNavigate();
  //  /products/new has no param at all; a numeric one means editing.
  const productId = param ? Number(param) : undefined;

  const categories = useCategories();
  const swatches   = useSwatches();
  // The list is already fetched for the products screen; reading the row from
  // it avoids a second endpoint for a form that is opened from that table.
  const products   = useAdminProducts({ pageSize: 100 });
  const save       = useSaveProduct(productId);

  const existing = productId ? products.data?.items.find(p => p.productId === productId) : undefined;

  const [form, setForm] = useState<{
    name: string; categoryId: string; price: string; kicker: string; blurb: string;
    longDescription: string; pieces: string; stitchingDays: string; stock: string;
    lowStockAt: string; defaultSwatchId: number | null; swatchIds: number[]; isActive: boolean;
    loaded: boolean;
  }>({
    name: '', categoryId: '', price: '', kicker: '', blurb: '', longDescription: '',
    pieces: '', stitchingDays: '3', stock: '0', lowStockAt: '6',
    defaultSwatchId: null, swatchIds: [], isActive: true, loaded: false,
  });

  const [errors, setErrors] = useState<FieldErrors>({});
  const [formError, setFormError] = useState<string | null>(null);

  // Seeded once from the fetched row, then owned by the form. Deriving every
  // field during render would fight the user's typing on each refetch.
  if (productId && existing && !form.loaded) {
    setForm({
      name: existing.name,
      categoryId: String(existing.categoryId),
      price: String(existing.price),
      kicker: existing.kicker ?? '',
      blurb: existing.blurb ?? '',
      longDescription: '',
      pieces: existing.pieces ?? '',
      stitchingDays: String(existing.stitchingDays),
      stock: String(existing.stock),
      lowStockAt: String(existing.lowStockAt),
      defaultSwatchId: existing.defaultSwatchId ?? null,
      swatchIds: swatches.data?.map(s => s.swatchId) ?? [],
      isActive: existing.isActive,
      loaded: true,
    });
  }

  if (categories.isPending || swatches.isPending || (productId && products.isPending)) {
    return <Loading label="Opening the editor…" />;
  }

  if (productId && products.data && !existing) {
    return <ErrorState title="That product is not in the shop" />;
  }

  const palette = swatches.data ?? [];
  const preview = palette.find(s => s.swatchId === form.defaultSwatchId) ?? palette[0];

  function toggleSwatch(swatchId: number) {
    setForm(f => ({
      ...f,
      swatchIds: f.swatchIds.includes(swatchId)
        ? f.swatchIds.filter(x => x !== swatchId)
        : [...f.swatchIds, swatchId],
    }));
  }

  function submit(event: React.FormEvent) {
    event.preventDefault();
    setFormError(null);

    // Mirrors ProductEditorRequest; the API and the procedure check it again.
    const found: FieldErrors = {};
    if (form.name.trim().length < 2) found.name = 'Give the product a name.';
    if (!form.categoryId) found.categoryId = 'Pick a category.';
    const price = Number(form.price);
    if (!Number.isFinite(price) || price < 0) found.price = 'Enter a price in rupees.';
    const stock = Number(form.stock);
    if (!Number.isFinite(stock) || stock < 0) found.stock = 'Stock cannot be negative.';

    setErrors(found);
    if (Object.keys(found).length) return;

    save.mutate(
      {
        name: form.name.trim(),
        categoryId: Number(form.categoryId),
        price,
        kicker: form.kicker.trim() || null,
        blurb: form.blurb.trim() || null,
        longDescription: form.longDescription.trim() || null,
        pieces: form.pieces.trim() || null,
        stitchingDays: Number(form.stitchingDays) || 0,
        stock,
        lowStockAt: Number(form.lowStockAt) || 0,
        defaultSwatchId: form.defaultSwatchId,
        swatchIds: form.swatchIds,
        isActive: form.isActive,
      },
      {
        onSuccess: () => navigate('/shop-panel/products'),
        onError: (e: unknown) => {
          if (e instanceof ApiError && e.isValidation && Object.keys(e.fieldErrors).length) {
            setErrors(fromApiErrors(e.fieldErrors));
          } else {
            setFormError(e instanceof ApiError ? e.message : 'Could not save the product.');
          }
        },
      },
    );
  }

  return (
    <>
      <header className="mb-5 flex flex-wrap items-center justify-between gap-5">
        <div>
          <h1 className="m-0 text-4xl">{productId ? 'Product editor' : 'New product'}</h1>
          <div className="mt-[3px] text-sm text-neutral-600">
            What customers see on the shop page
          </div>
        </div>
      </header>

      <div className="grid grid-cols-1 items-start gap-4 3xl:grid-cols-[1fr_330px]">
        <form className="card mb-4 bg-bg p-5 shadow-sm" onSubmit={submit} noValidate>
          <div className="grid grid-cols-1 gap-3.5 sm:grid-cols-2">
            <div className="sm:col-span-2">
              <Field label="Product name" error={errors.name}>
                {p => <input {...p} className="input" value={form.name}
                             onChange={e => setForm({ ...form, name: e.target.value })} />}
              </Field>
            </div>

            <Field label="Category" error={errors.categoryId}>
              {p => (
                <select {...p} className="input" value={form.categoryId}
                        onChange={e => setForm({ ...form, categoryId: e.target.value })}>
                  <option value="">Choose…</option>
                  {categories.data?.map(c => <option key={c.categoryId} value={c.categoryId}>{c.name}</option>)}
                </select>
              )}
            </Field>

            <Field label="Price (Rs)" error={errors.price}>
              {p => <input {...p} className="input" type="number" min={0} step="1" value={form.price}
                           onChange={e => setForm({ ...form, price: e.target.value })} />}
            </Field>

            <Field label="Pieces in set">
              {p => <input {...p} className="input" placeholder="12 pieces" value={form.pieces}
                           onChange={e => setForm({ ...form, pieces: e.target.value })} />}
            </Field>

            <Field label="Stitching days">
              {p => <input {...p} className="input" type="number" min={0} max={90} value={form.stitchingDays}
                           onChange={e => setForm({ ...form, stitchingDays: e.target.value })} />}
            </Field>

            <Field label="Stock" error={errors.stock}>
              {p => <input {...p} className="input" type="number" min={0} value={form.stock}
                           disabled={Boolean(productId)}
                           onChange={e => setForm({ ...form, stock: e.target.value })} />}
            </Field>

            <Field label="Low-stock threshold">
              {p => <input {...p} className="input" type="number" min={0} value={form.lowStockAt}
                           onChange={e => setForm({ ...form, lowStockAt: e.target.value })} />}
            </Field>

            <div className="sm:col-span-2">
              <Field label="Kicker" hint="The small label above the name — 'Bridal bedding', 'Parde'">
                {p => <input {...p} className="input" value={form.kicker}
                             onChange={e => setForm({ ...form, kicker: e.target.value })} />}
              </Field>
            </div>

            <div className="sm:col-span-2">
              <Field label="Description">
                {p => <textarea {...p} className="input" rows={3} value={form.blurb}
                                onChange={e => setForm({ ...form, blurb: e.target.value })} />}
              </Field>
            </div>
          </div>

          {productId && (
            <p className="mb-2 mt-0 text-xs text-neutral-600">
              Stock is changed from the Inventory screen, where every movement is recorded with a reason.
            </p>
          )}

          <div className="mt-4">
            <h2 className="mb-1 text-[19px]">Fabric swatches</h2>
            <p className="mb-3 text-[13px] text-neutral-600">
              The cloths this product can be stitched in. Tap to include or remove. Real photographs
              can be dropped in later without changing the layout.
            </p>

            <div className="mb-4 flex flex-wrap gap-3">
              {palette.map(s => (
                <Swatch
                  key={s.swatchId}
                  name={s.name}
                  colorValue={s.colorValue}
                  weave={s.weave}
                  selected={form.swatchIds.includes(s.swatchId)}
                  onSelect={() => toggleSwatch(s.swatchId)}
                />
              ))}
            </div>

            <Field label="Default cloth — the one the shop page opens on">
              {p => (
                <select
                  {...p}
                  className="input"
                  value={form.defaultSwatchId ?? ''}
                  onChange={e => setForm({ ...form, defaultSwatchId: e.target.value ? Number(e.target.value) : null })}
                >
                  <option value="">None</option>
                  {palette
                    .filter(s => form.swatchIds.includes(s.swatchId))
                    .map(s => <option key={s.swatchId} value={s.swatchId}>{s.name}</option>)}
                </select>
              )}
            </Field>

            <label className="mt-3 flex items-center gap-2.5 text-sm">
              <input
                type="checkbox" checked={form.isActive}
                onChange={e => setForm({ ...form, isActive: e.target.checked })}
                className="h-[18px] w-[18px] accent-accent"
              />
              Show in the shop
            </label>

            {/*  Only once the product exists: the upload posts to
                /admin/products/{id}/image, and a product being created has no
                id to post to yet. Save first, then add the photo.          */}
            {productId ? (
              <div className="mt-4 sm:col-span-2">
                <PhotoField
                  productId={productId}
                  imagePath={existing?.imagePath}
                  swatchColorValue={preview?.colorValue}
                  swatchWeave={preview?.weave}
                />
              </div>
            ) : (
              <p className="mt-4 text-xs text-neutral-600 sm:col-span-2">
                Save the product first, then you can add a photo to it.
              </p>
            )}
          </div>

          {formError && (
            <div role="alert" className="mt-3.5 rounded-md bg-accent-100 px-4 py-3 text-sm text-accent-800">{formError}</div>
          )}

          <div className="mt-5 flex gap-2.5">
            <button type="submit" className="btn btn-primary" disabled={save.isPending}>
              {save.isPending ? 'Saving…' : 'Save product'}
            </button>
            <button type="button" className="btn btn-secondary" onClick={() => navigate('/shop-panel/products')}>
              Cancel
            </button>
          </div>
        </form>

        {/* the live shop preview from the design */}
        <aside className="card gap-0 overflow-hidden bg-bg p-0 shadow-md">
          <div
            className="washed h-[210px]" aria-hidden="true"
            style={{ background: fabricBackground(preview?.colorValue, preview?.weave) }}
          />
          <div className="px-5 pb-5 pt-4">
            <div className="card-kicker">Shop preview</div>
            <div className="mb-1.5 mt-1 font-heading text-[21px]">
              {form.name || 'Untitled product'}
            </div>
            <p className="mb-3 mt-0 text-[13px] text-neutral-700">
              {form.blurb || 'A line about the cloth goes here.'}
            </p>
            <div className="text-[17px] font-bold">
              {formatPkr(Number(form.price) || 0)}
            </div>
          </div>
        </aside>
      </div>
    </>
  );
}
