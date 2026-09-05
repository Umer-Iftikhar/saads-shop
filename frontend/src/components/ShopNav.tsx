import { NavLink, Link } from 'react-router-dom';
import { Logo } from './Logo';
import { useCart } from '../state/cart';

const LINKS = [
  { to: '/',                 label: 'Home',           end: true },
  { to: '/wedding-sets',     label: 'Wedding sets',   end: false },
  { to: '/bistar-parde',     label: 'Bistar & parde', end: false },
  { to: '/build-your-set',   label: 'Build your set', end: false },
];

/**
 * The storefront header: the announcement strip, then the nav bar.
 *
 * The strip's copy is editable in the shop panel and arrives with the public
 * settings, so it is passed in rather than hard-coded.
 */
export function ShopNav({ banner }: { banner?: string | null }) {
  const { count } = useCart();

  return (
    <header>
      {banner && (
        <div
          style={{
            background: 'var(--color-accent-2-700)',
            color: 'var(--color-accent-2-100)',
            fontSize: 13,
            textAlign: 'center',
            padding: '8px 16px',
          }}
        >
          {banner}
        </div>
      )}

      <div
        style={{
          background: 'var(--color-bg)',
          borderBottom: '1px solid var(--color-divider)',
          position: 'sticky',
          top: 0,
          zIndex: 20,
        }}
      >
        <nav
          className="page page-pad"
          aria-label="Shop"
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: 26,
            padding: '14px 40px',
            flexWrap: 'wrap',
          }}
        >
          <Link
            to="/"
            style={{ display: 'flex', alignItems: 'center', gap: 11, marginRight: 'auto', color: 'inherit' }}
          >
            <Logo size={44} />
            <span>
              <span
                style={{
                  display: 'block',
                  fontFamily: 'var(--font-heading)',
                  fontSize: 20,
                  lineHeight: 1,
                }}
              >
                Saad&rsquo;s Shop
              </span>
              <span
                style={{
                  display: 'block',
                  fontSize: 10,
                  letterSpacing: '0.16em',
                  textTransform: 'uppercase',
                  color: 'var(--color-neutral-600)',
                  marginTop: 3,
                }}
              >
                Raja Bazaar · Rawalpindi
              </span>
            </span>
          </Link>

          {LINKS.map(link => (
            <NavLink
              key={link.to}
              to={link.to}
              end={link.end}
              style={({ isActive }) => ({
                fontSize: 14,
                color: isActive ? 'var(--color-accent-700)' : 'var(--color-text)',
                fontWeight: isActive ? 700 : 400,
              })}
            >
              {link.label}
            </NavLink>
          ))}

          <Link
            to="/cart"
            className="btn btn-secondary"
            style={{ padding: '7px 16px', fontSize: 14 }}
            // The count is in the label rather than only in the text, so a
            // screen reader announces "Cart, 2 items" instead of "Cart 2".
            aria-label={`Cart, ${count} ${count === 1 ? 'item' : 'items'}`}
          >
            Cart · {count}
          </Link>
        </nav>
      </div>
    </header>
  );
}
