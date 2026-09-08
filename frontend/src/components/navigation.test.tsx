import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ReactNode } from 'react';
import { RequireAuth } from './admin/RequireAuth';
import { AdminSidebar } from './admin/AdminSidebar';
import { ShopNav } from './ShopNav';
import { CartProvider, useCart } from '../state/cart';
import type { CurrentUser } from '../state/auth';

//  The gate and the sidebar both read the session, and neither owns it — so
//  the session is stubbed here and the real thing is exercised in
//  state/auth.test.tsx. Mocking it keeps these tests about what is on screen.
const authState = vi.hoisted(() => ({
  current: {} as { user?: CurrentUser | null; isOwner?: boolean; bootstrapping?: boolean },
}));
const signOut = vi.hoisted(() => vi.fn());

vi.mock('../state/auth', () => ({
  useAuth: () => ({
    user: null, bootstrapping: false, isOwner: false,
    signIn: vi.fn(), signOut, refresh: vi.fn(),
    ...authState.current,
  }),
}));

const anOwner = { userId: 'u1', email: 'saad@saadsshop.pk', fullName: 'Saad', roles: ['Owner'] };
const aStaffMember = { userId: 'u2', email: 'nadia@saadsshop.pk', fullName: 'Nadia', roles: ['Staff'] };

function signedInAs(user: typeof anOwner | null, isOwner = false, bootstrapping = false) {
  authState.current = { user, isOwner, bootstrapping };
}

beforeEach(() => { authState.current = {}; signOut.mockReset(); });
afterEach(() => vi.unstubAllGlobals());

describe('RequireAuth', () => {
  const Panel = () => <p>The takings for today</p>;

  function renderGate(children: ReactNode, at = '/shop-panel') {
    return render(
      <MemoryRouter initialEntries={[at]}>
        <Routes>
          <Route path="/shop-panel/sign-in" element={<h1>Sign in</h1>} />
          <Route path="/shop-panel" element={children} />
          <Route path="/shop-panel/settings" element={children} />
        </Routes>
      </MemoryRouter>,
    );
  }

  it('waits rather than deciding while the session is still being recovered', () => {
    // The flash of a sign-in page at someone who is signed in is the bug this
    // prevents: the session comes back from an async refresh on mount.
    signedInAs(null, false, true);

    renderGate(<RequireAuth><Panel /></RequireAuth>);

    expect(screen.getByRole('status')).toHaveTextContent('Opening the shop panel…');
    expect(screen.queryByText('Sign in')).not.toBeInTheDocument();
    expect(screen.queryByText('The takings for today')).not.toBeInTheDocument();
  });

  it('sends a stranger to the sign-in screen', () => {
    signedInAs(null);

    renderGate(<RequireAuth><Panel /></RequireAuth>);

    expect(screen.getByRole('heading', { name: 'Sign in' })).toBeInTheDocument();
    expect(screen.queryByText('The takings for today')).not.toBeInTheDocument();
  });

  it('lets a signed-in member of staff through', () => {
    signedInAs(aStaffMember);

    renderGate(<RequireAuth><Panel /></RequireAuth>);

    expect(screen.getByText('The takings for today')).toBeInTheDocument();
  });

  it('keeps staff out of an owner-only screen, and says why', () => {
    signedInAs(aStaffMember, false);

    renderGate(<RequireAuth ownerOnly><Panel /></RequireAuth>, '/shop-panel/settings');

    expect(screen.getByRole('heading', { name: /That screen is Saad/ })).toBeInTheDocument();
    expect(screen.queryByText('The takings for today')).not.toBeInTheDocument();
    // Not bounced to sign-in: they are signed in, just not the owner.
    expect(screen.queryByText('Sign in')).not.toBeInTheDocument();
  });

  it('lets the owner into an owner-only screen', () => {
    signedInAs(anOwner, true);

    renderGate(<RequireAuth ownerOnly><Panel /></RequireAuth>, '/shop-panel/settings');

    expect(screen.getByText('The takings for today')).toBeInTheDocument();
  });

  it('is not the security boundary — the API is', () => {
    // Documented in the component, asserted here so nobody deletes the server
    // check on the strength of this gate: it renders markup, nothing more.
    signedInAs(aStaffMember, false);

    const { container } = renderGate(
      <RequireAuth ownerOnly><Panel /></RequireAuth>, '/shop-panel/settings');

    expect(container.textContent).not.toContain('The takings for today');
  });
});

describe('AdminSidebar', () => {
  const renderSidebar = (props: Parameters<typeof AdminSidebar>[0] = {}) =>
    render(<MemoryRouter initialEntries={['/shop-panel/orders']}>
      <AdminSidebar {...props} />
    </MemoryRouter>);

  it('is a landmark a keyboard user can jump to', () => {
    signedInAs(anOwner, true);

    renderSidebar();

    expect(screen.getByRole('navigation', { name: 'Shop panel' })).toBeInTheDocument();
  });

  it('hides Settings from staff, rather than offering a button that always fails', () => {
    signedInAs(aStaffMember, false);

    renderSidebar();

    expect(screen.queryByRole('link', { name: 'Settings' })).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Orders' })).toBeInTheDocument();
  });

  it('shows Settings to the owner', () => {
    signedInAs(anOwner, true);

    renderSidebar();

    expect(screen.getByRole('link', { name: 'Settings' }))
      .toHaveAttribute('href', '/shop-panel/settings');
  });

  it('marks the screen being looked at', () => {
    signedInAs(anOwner, true);

    renderSidebar();

    expect(screen.getByRole('link', { name: /Orders/ }).className).toContain('is-active');
    expect(screen.getByRole('link', { name: 'Products' }).className).not.toContain('is-active');
  });

  it('does not mark Overview active on every screen beneath it', () => {
    // Without end, "/shop-panel" prefix-matches every panel route.
    signedInAs(anOwner, true);

    renderSidebar();

    expect(screen.getByRole('link', { name: 'Overview' }).className).not.toContain('is-active');
  });

  it('badges the counts it is given, and only those', () => {
    signedInAs(anOwner, true);

    renderSidebar({ counts: { orders: 3, lowStock: 0 } });

    expect(within(screen.getByRole('link', { name: /Orders/ })).getByText('3')).toBeInTheDocument();
    // A zero badge is a badge saying nothing is wrong, which is noise.
    expect(screen.getByRole('link', { name: 'Inventory' }).textContent).toBe('Inventory');
  });

  it('says who is signed in and in what capacity', () => {
    signedInAs(anOwner, true);

    renderSidebar();

    expect(screen.getByText(/Saad · owner/)).toBeInTheDocument();
  });

  it('calls a member of staff staff', () => {
    signedInAs(aStaffMember, false);

    renderSidebar();

    expect(screen.getByText(/Nadia · staff/)).toBeInTheDocument();
  });

  it('signs out', async () => {
    signedInAs(anOwner, true);

    renderSidebar();
    await userEvent.click(screen.getByRole('button', { name: 'Sign out' }));

    expect(signOut).toHaveBeenCalledOnce();
  });

  it('closes the drawer when a link is followed on a phone', async () => {
    signedInAs(anOwner, true);
    const onNavigate = vi.fn();

    renderSidebar({ onNavigate, open: true });
    await userEvent.click(screen.getByRole('link', { name: 'Products' }));

    expect(onNavigate).toHaveBeenCalled();
  });

  it('is invisible when closed, so its links leave the tab order', () => {
    // A drawer merely pushed off-screen still holds focusable links.
    signedInAs(anOwner, true);

    const { container } = renderSidebar({ open: false });

    expect(container.querySelector('#admin-nav')!.className).toContain('max-lg:invisible');
  });

  it('is visible when opened', () => {
    signedInAs(anOwner, true);

    const { container } = renderSidebar({ open: true });

    expect(container.querySelector('#admin-nav')!.className).toContain('max-lg:visible');
  });
});

describe('ShopNav', () => {
  function Adder({ times }: { times: number }) {
    const { add, items } = useCart();
    if (items.length === 0 && times > 0) {
      add({
        productId: 1, name: 'Gulaab Bridal Set', price: 18_500,
        slug: 'gulaab-bridal-set', quantity: times,
      });
    }
    return null;
  }

  const renderNav = (banner?: string | null, cartItems = 0) =>
    render(
      <MemoryRouter initialEntries={['/wedding-sets']}>
        <CartProvider>
          <Adder times={cartItems} />
          <ShopNav banner={banner} />
        </CartProvider>
      </MemoryRouter>,
    );

  it('names the cart count for a screen reader, not just for the eye', () => {
    renderNav(null, 2);

    // "Cart · 2" read aloud is "Cart 2", which is not a sentence.
    expect(screen.getByRole('link', { name: 'Cart, 2 items' })).toBeInTheDocument();
  });

  it('says "1 item", not "1 items"', () => {
    renderNav(null, 1);

    expect(screen.getByRole('link', { name: 'Cart, 1 item' })).toBeInTheDocument();
  });

  it('says an empty cart is empty rather than hiding the link', () => {
    renderNav();

    expect(screen.getByRole('link', { name: 'Cart, 0 items' })).toHaveAttribute('href', '/cart');
  });

  it('shows the announcement the shop wrote', () => {
    renderNav('Free delivery in Rawalpindi this week');

    expect(screen.getByText('Free delivery in Rawalpindi this week')).toBeInTheDocument();
  });

  it('shows no strip at all when there is nothing to announce', () => {
    renderNav(null);

    expect(screen.queryByText(/Free delivery/)).not.toBeInTheDocument();
  });

  it('marks the section being browsed', () => {
    renderNav();

    expect(screen.getByRole('link', { name: 'Wedding sets' }).className).toContain('font-bold');
    expect(screen.getByRole('link', { name: 'Home' }).className).not.toContain('font-bold');
  });

  it('offers every part of the shop', () => {
    renderNav();

    const nav = screen.getByRole('navigation', { name: 'Shop' });
    for (const label of ['Home', 'Wedding sets', 'Bistar & parde', 'Build your set']) {
      expect(within(nav).getByRole('link', { name: label })).toBeInTheDocument();
    }
  });
});
