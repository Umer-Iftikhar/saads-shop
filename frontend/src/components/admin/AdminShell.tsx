import { useEffect, useState } from 'react';
import { Outlet, useLocation } from 'react-router-dom';
import { AdminSidebar } from './AdminSidebar';
import { useDashboard } from '../../hooks/adminQueries';

/**
 * The shop panel's frame: sidebar beside the working area on a desktop, a
 * drawer over it on a phone.
 *
 * The badge counts come from the dashboard query the Overview screen already
 * uses — same query key, so TanStack Query serves both from one request rather
 * than asking the server twice for the same numbers.
 */
export function AdminShell() {
  const [drawerOpen, setDrawerOpen] = useState(false);
  const { pathname } = useLocation();

  // The panel is one of the few places where a failed request is unremarkable:
  // if the dashboard is briefly unavailable the navigation still works, it just
  // loses its badges.
  const dashboard = useDashboard();
  const stats = dashboard.data?.stats;

  // Navigating closes the drawer. Leaving it open over the screen the
  // shopkeeper just asked for is the classic mobile-nav bug.
  useEffect(() => { setDrawerOpen(false); }, [pathname]);

  // Escape closes it too — the drawer is modal in effect, so it should behave
  // like every other modal thing on the page.
  useEffect(() => {
    if (!drawerOpen) return;
    const onKey = (event: KeyboardEvent) => { if (event.key === 'Escape') setDrawerOpen(false); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [drawerOpen]);

  return (
    <div className="grid min-h-screen grid-cols-1 bg-neutral-100 lg:grid-cols-[var(--container-sidebar)_1fr]">
      <AdminSidebar
        open={drawerOpen}
        counts={{
          orders: stats?.ordersAwaitingMeasurements,
          jobs: stats?.jobsOnFloor,
        }}
        onNavigate={() => setDrawerOpen(false)}
      />

      {drawerOpen && (
        <button
          type="button"
          className="fixed inset-0 z-30 border-0 bg-neutral-900/50 lg:hidden"
          aria-label="Close the menu"
          onClick={() => setDrawerOpen(false)}
        />
      )}

      <main id="main" className="min-w-0 px-4 py-5 lg:px-8 lg:py-7">
        <button
          type="button"
          className="btn btn-secondary mb-4 inline-flex lg:hidden"
          aria-expanded={drawerOpen}
          aria-controls="admin-nav"
          onClick={() => setDrawerOpen(true)}
        >
          ☰ Menu
        </button>

        <Outlet />
      </main>
    </div>
  );
}
