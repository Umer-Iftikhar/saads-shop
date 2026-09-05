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
    <div role="status" aria-live="polite" style={{ padding: '64px 0', textAlign: 'center', color: 'var(--color-neutral-700)' }}>
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
          style={{
            height,
            borderRadius: 'calc(var(--radius-lg) * 1.15)',
            background: 'var(--color-neutral-200)',
          }}
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
    <div role="alert" style={{ padding: '56px 0', textAlign: 'center' }}>
      <h2 style={{ fontSize: 26, marginBottom: 8 }}>{title}</h2>
      {detail && <p style={{ color: 'var(--color-neutral-700)', maxWidth: '48ch', margin: '0 auto 20px' }}>{detail}</p>}
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
    <div style={{ padding: '56px 0', textAlign: 'center' }}>
      <h2 style={{ fontSize: 26, marginBottom: 8 }}>{title}</h2>
      {detail && <p style={{ color: 'var(--color-neutral-700)', maxWidth: '48ch', margin: '0 auto 20px' }}>{detail}</p>}
      {action}
    </div>
  );
}
