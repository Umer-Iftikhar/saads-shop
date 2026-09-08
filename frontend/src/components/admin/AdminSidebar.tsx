import { NavLink } from 'react-router-dom';
import { Logo } from '../Logo';
import { useAuth } from '../../state/auth';

/**
 * The shop panel's sidebar, from frame 07's design: dark ground, pill nav
 * items, the active one in terracotta, counts as small badges.
 *
 * Badge counts come from live data rather than the design's hard-coded numbers,
 * so "3 need attention" is actually three.
 */
export function AdminSidebar({ counts, onNavigate, open = false }: {
  counts?: { orders?: number; lowStock?: number; jobs?: number };
  onNavigate?: () => void;
  /** Only meaningful below 860px, where the sidebar is a drawer. */
  open?: boolean;
}) {
  const { user, isOwner, signOut } = useAuth();

  const items: { to: string; label: string; badge?: number; ownerOnly?: boolean }[] = [
    { to: '/shop-panel',            label: 'Overview' },
    { to: '/shop-panel/orders',     label: 'Orders',          badge: counts?.orders },
    { to: '/shop-panel/inventory',  label: 'Inventory',       badge: counts?.lowStock },
    { to: '/shop-panel/products',   label: 'Products' },
    { to: '/shop-panel/stitching',  label: 'Stitching queue', badge: counts?.jobs },
    { to: '/shop-panel/customers',  label: 'Customers' },
    { to: '/shop-panel/settings',   label: 'Settings',        ownerOnly: true },
  ];

  // A drawer below the panel's split and a permanent column above it. Hidden
  // rather than merely translated when closed: a sidebar pushed off-screen
  // still holds its links in the tab order.
  return (
    <nav
      id="admin-nav"
      aria-label="Shop panel"
      className={`sticky top-0 z-40 flex h-screen flex-col gap-1.5 overflow-y-auto
                  bg-neutral-900 px-4 py-6 text-neutral-200
                  max-lg:fixed max-lg:inset-y-0 max-lg:left-0 max-lg:w-[280px]
                  max-lg:transition-[transform,visibility] max-lg:duration-200
                  ${open
                    ? 'max-lg:visible max-lg:translate-x-0 max-lg:shadow-lg'
                    : 'max-lg:invisible max-lg:-translate-x-full'}`}
    >
      <div className="flex items-center gap-2.5 px-2 pb-6">
        <Logo size={36} />
        <div>
          <div className="font-heading text-base text-neutral-100">
            Saad&rsquo;s Shop
          </div>
          <div className="text-[10px] uppercase tracking-[0.14em] text-neutral-500">
            Shop panel
          </div>
        </div>
      </div>

      {items
        // Settings changes how the shop takes money, so it is not even shown to
        // staff — the API refuses it too, but a button that always fails is a
        // worse experience than no button.
        .filter(item => !item.ownerOnly || isOwner)
        .map(item => (
          <NavLink
            key={item.to}
            to={item.to}
            end={item.to === '/shop-panel'}
            onClick={onNavigate}
            className={({ isActive }) => `admin-nav-item${isActive ? ' is-active' : ''}`}
          >
            <span>{item.label}</span>
            {item.badge ? <span className="admin-nav-badge">{item.badge}</span> : null}
          </NavLink>
        ))}

      <div className="mt-auto px-2.5 pt-4">
        <div className="text-xs text-neutral-500">
          {user?.fullName ?? 'Signed in'} · {isOwner ? 'owner' : 'staff'}
        </div>
        <button
          type="button"
          className="btn btn-ghost mt-1 px-0 text-neutral-300"
          onClick={() => void signOut()}
        >
          Sign out
        </button>
      </div>
    </nav>
  );
}
