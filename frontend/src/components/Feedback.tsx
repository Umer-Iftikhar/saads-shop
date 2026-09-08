import type { ReactNode } from 'react';

/**
 * Loading, empty and error states.
 *
 * Collected here because they are the screens people actually hit on a bad
 * connection in a bazaar, and because a half-designed error state is how an app
 * ends up showing "undefined" to a customer.
 */

export function Loading({ label = 'Loading…' }: { label?: string }) {
  return (
    <div role="status" aria-live="polite" className="py-16 text-center text-neutral-700">
      {label}
    </div>
  );
}

/** Placeholder cards, so a slow catalogue does not collapse the layout. */
export function CardSkeleton({ count = 3, height = 300 }: { count?: number; height?: number }) {
  return (
    <>
      {Array.from({ length: count }, (_, i) => (
        <div
          key={i}
          aria-hidden="true"
          className="rounded-card bg-neutral-200"
          // The only thing here the caller varies, and it varies per grid.
          style={{ height }}
        />
      ))}
    </>
  );
}

export function ErrorState({ title, detail, onRetry }: {
  title: string;
  detail?: ReactNode;
  onRetry?: () => void;
}) {
  return (
    <div role="alert" className="py-14 text-center">
      <h2 className="mb-2 text-[26px]">{title}</h2>
      {detail && <p className="mx-auto mb-5 max-w-[48ch] text-neutral-700">{detail}</p>}
      {onRetry && (
        <button type="button" className="btn btn-secondary" onClick={onRetry}>
          Try again
        </button>
      )}
    </div>
  );
}

export function EmptyState({ title, detail, action }: {
  title: string;
  detail?: string;
  action?: ReactNode;
}) {
  return (
    <div className="py-14 text-center">
      <h2 className="mb-2 text-[26px]">{title}</h2>
      {detail && <p className="mx-auto mb-5 max-w-[48ch] text-neutral-700">{detail}</p>}
      {action}
    </div>
  );
}
