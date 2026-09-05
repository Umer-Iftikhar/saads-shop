import type { ReactNode } from 'react';
import { useId } from 'react';

/**
 * A labelled input that wires up its own error.
 *
 * The message is tied to the control with `aria-describedby` and the control is
 * marked `aria-invalid`, so a screen reader announces the problem with the
 * field rather than leaving it as red text floating nearby.
 */
export function Field({
  label, error, hint, children,
}: {
  label: string;
  error?: string;
  hint?: string;
  children: (props: {
    id: string;
    'aria-invalid': boolean | undefined;
    'aria-describedby': string | undefined;
  }) => ReactNode;
}) {
  const id = useId();
  const errorId = `${id}-error`;
  const hintId  = `${id}-hint`;

  const describedBy = [error ? errorId : null, hint ? hintId : null].filter(Boolean).join(' ') || undefined;

  return (
    <div className="block mb-2.5">
      <label htmlFor={id} className="block mb-1 text-xs text-text/70">{label}</label>

      {children({
        id,
        'aria-invalid': error ? true : undefined,
        'aria-describedby': describedBy,
      })}

      {hint && !error && (
        <div id={hintId} className="mt-1 text-xs text-neutral-600">
          {hint}
        </div>
      )}

      {error && <div id={errorId} className="field-error">{error}</div>}
    </div>
  );
}
