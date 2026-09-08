import { useRef, useState } from 'react';
import { ApiError } from '../../lib/api';
import { fabricBackground } from '../../lib/fabric';
import { useRemoveProductImage, useUploadProductImage } from '../../hooks/adminQueries';

/** What the API accepts. Kept in step with ProductImageService.Accepted. */
const ACCEPTED = ['.jpg', '.jpeg', '.png', '.webp'];
const MAX_BYTES = 10 * 1024 * 1024;

/**
 * The product photograph, uploaded from the shop's phone or laptop.
 *
 * The checks here are a courtesy, not a defence — the server repeats every one
 * of them against the file's actual bytes, because anything the browser decides
 * can be skipped entirely. What they buy is telling a shopkeeper that a 40MB
 * photo is too big *before* they spend three minutes uploading it over the
 * shop's connection.
 */
export function PhotoField({ productId, imagePath, swatchColorValue, swatchWeave }: {
  productId: number;
  imagePath?: string | null;
  swatchColorValue?: string | null;
  swatchWeave?: string | null;
}) {
  const input = useRef<HTMLInputElement>(null);
  const [error, setError] = useState<string | null>(null);

  const upload = useUploadProductImage(productId);
  const remove = useRemoveProductImage(productId);
  const busy = upload.isPending || remove.isPending;

  function choose(file: File | undefined) {
    setError(null);
    if (!file) return;

    const extension = file.name.slice(file.name.lastIndexOf('.')).toLowerCase();

    if (!ACCEPTED.includes(extension)) {
      setError(`That is a ${extension || 'file with no extension'}. Use a JPG, PNG or WEBP.`);
      return;
    }

    if (file.size > MAX_BYTES) {
      setError(`That photo is ${Math.round(file.size / 1024 / 1024)} MB. Please use one under 10 MB.`);
      return;
    }

    upload.mutate(file, {
      onError: e => setError(e instanceof ApiError ? e.message : 'Could not upload that photo.'),
      // Otherwise choosing the same file again after a failure does nothing:
      // the input's value has not changed, so it fires no event.
      onSettled: () => { if (input.current) input.current.value = ''; },
    });
  }

  return (
    <div>
      <span className="mb-1 block text-xs text-neutral-700">Photo</span>

      <div className="flex items-start gap-4">
        {imagePath ? (
          <img
            src={imagePath}
            alt="The product photo as customers see it"
            className="h-[110px] w-[110px] flex-none rounded-xl object-cover"
          />
        ) : (
          <div
            className="washed h-[110px] w-[110px] flex-none rounded-xl"
            style={{ background: fabricBackground(swatchColorValue, swatchWeave) }}
            // The text beside it already says there is no photo.
            aria-hidden="true"
          />
        )}

        <div className="flex-1">
          <p className="mb-2 mt-0 text-xs text-neutral-600">
            {imagePath
              ? 'Customers see this on the shop page.'
              : 'No photo yet, so the shop draws the cloth instead. JPG, PNG or WEBP, up to 10 MB.'}
          </p>

          <div className="flex flex-wrap gap-2.5">
            <button
              type="button" className="btn btn-secondary" disabled={busy}
              onClick={() => input.current?.click()}
            >
              {upload.isPending ? 'Uploading…' : imagePath ? 'Replace photo' : 'Add a photo'}
            </button>

            {imagePath && (
              <button
                type="button" className="btn btn-ghost" disabled={busy}
                onClick={() => {
                  setError(null);
                  remove.mutate(undefined, {
                    onError: e => setError(e instanceof ApiError ? e.message : 'Could not remove the photo.'),
                  });
                }}
              >
                Remove
              </button>
            )}
          </div>

          {/*  Visually hidden rather than display:none — a hidden input cannot
              be clicked open by the button above in every browser.          */}
          <input
            ref={input}
            type="file"
            className="sr-only"
            accept={ACCEPTED.join(',')}
            aria-label="Choose a product photo"
            onChange={e => choose(e.target.files?.[0])}
          />

          {error && <div role="alert" className="field-error mt-2">{error}</div>}
        </div>
      </div>
    </div>
  );
}
