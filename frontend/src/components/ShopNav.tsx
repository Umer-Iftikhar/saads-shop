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
        <div className="bg-accent-2-700 px-4 py-2 text-center text-[13px] text-accent-2-100">
          {banner}
        </div>
      )}

      <div className="sticky top-0 z-20 border-b border-divider bg-bg">
        <nav
          aria-label="Shop"
          className="mx-auto flex max-w-storefront flex-wrap items-center gap-6
                     px-4 py-3.5 sm:px-6 xl:px-10"
        >
          <Link to="/" className="mr-auto flex items-center gap-2.5 text-text hover:no-underline">
            <Logo size={44} />
            <span>
              <span className="block font-heading text-xl/none">Saad&rsquo;s Shop</span>
              <span className="mt-[3px] block text-[10px] uppercase tracking-[0.16em] text-neutral-600">
                Raja Bazaar · Rawalpindi
              </span>
            </span>
          </Link>

          {LINKS.map(link => (
            <NavLink
              key={link.to}
              to={link.to}
              end={link.end}
              className={({ isActive }) =>
                isActive ? 'text-sm font-bold text-accent-700' : 'text-sm text-text'}
            >
              {link.label}
            </NavLink>
          ))}

          <Link
            to="/cart"
            className="btn btn-secondary px-4 py-[7px] text-sm"
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
