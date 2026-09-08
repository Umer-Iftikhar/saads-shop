import { Navigate, useLocation } from 'react-router-dom';
import type { ReactNode } from 'react';
import { useAuth } from '../../state/auth';
import { Loading } from '../Feedback';

/**
 * Gates the shop panel.
 *
 * This is a convenience, not a security boundary — every admin endpoint checks
 * its own authorisation policy, so a determined visitor who edits their way
 * past this gets 401s and nothing else. What it buys is not showing an empty
 * panel to someone who cannot fill it.
 */
export function RequireAuth({ children, ownerOnly = false }: {
  children: ReactNode;
  ownerOnly?: boolean;
}) {
  const { user, bootstrapping, isOwner } = useAuth();
  const location = useLocation();

  // The session is recovered by an async refresh on mount. Rendering the
  // sign-in screen before that settles would flash the login page at someone
  // who is already signed in.
  if (bootstrapping) return <Loading label="Opening the shop panel…" />;

  if (!user) {
    // Remember where they were going, so signing in lands them there.
    return <Navigate to="/shop-panel/sign-in" state={{ from: location.pathname }} replace />;
  }

  if (ownerOnly && !isOwner) {
    return (
      <main id="main" className="px-8 py-16 text-center">
        <h1 className="mb-2 text-3xl">That screen is Saad&rsquo;s</h1>
        <p className="text-neutral-700">
          Settings and staff accounts are for the shop owner. Everything else on the panel is yours.
        </p>
      </main>
    );
  }

  return <>{children}</>;
}
