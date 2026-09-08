import { Outlet, Route, Routes, useLocation } from 'react-router-dom';
import { useEffect } from 'react';
import { ShopNav } from './components/ShopNav';
import { ShopFooter } from './pages/Home';
import { Home } from './pages/Home';
import { Listing } from './pages/Listing';
import { Product } from './pages/Product';
import { SetBuilder } from './pages/SetBuilder';
import { Cart } from './pages/Cart';
import { OrderPlaced } from './pages/OrderPlaced';
import { useShopSettings } from './hooks/queries';

import { AdminShell } from './components/admin/AdminShell';
import { RequireAuth } from './components/admin/RequireAuth';
import { SignIn } from './pages/admin/SignIn';
import { TwoFactorSetup } from './pages/admin/TwoFactorSetup';
import { Overview } from './pages/admin/Overview';
import { Orders } from './pages/admin/Orders';
import { OrderDetail } from './pages/admin/OrderDetail';
import { Inventory } from './pages/admin/Inventory';
import { Stitching } from './pages/admin/Stitching';
import { Customers } from './pages/admin/Customers';
import { Products } from './pages/admin/Products';
import { ProductEditor } from './pages/admin/ProductEditor';
import { Settings } from './pages/admin/Settings';

/**
 * Moves focus to the main region on navigation.
 *
 * A single-page app changes the view without a page load, so a screen reader
 * would otherwise keep reading from wherever the link was and a keyboard user
 * would tab from the old position. This is the SPA equivalent of the browser's
 * own behaviour.
 */
function FocusOnNavigate() {
  const { pathname } = useLocation();

  useEffect(() => {
    const main = document.getElementById('main');
    if (main) {
      main.setAttribute('tabindex', '-1');
      main.focus({ preventScroll: true });
    }
    window.scrollTo(0, 0);
  }, [pathname]);

  return null;
}

/**
 * The storefront's chrome. The shop panel deliberately does not sit inside it:
 * the staff screens have their own navigation, their own ground colour, and no
 * business showing a customer-facing footer.
 */
function Storefront() {
  const settings = useShopSettings();

  return (
    <>
      <ShopNav banner={settings.data?.bannerText} />
      <Outlet />
      <ShopFooter settings={settings.data} />
    </>
  );
}

export default function App() {
  return (
    <>
      <a className="skip-link" href="#main">Skip to content</a>
      <FocusOnNavigate />

      <Routes>
        <Route element={<Storefront />}>
          <Route path="/" element={<main id="main"><Home /></main>} />

          <Route
            path="/wedding-sets"
            element={
              <Listing
                fixedCategory="wedding-sets"
                title="Wedding sets"
                subtitle="Bridal bedding and full room packages · stitching and delivery inside Rawalpindi"
              />
            }
          />

          {/* The design's "everything else" view — the whole catalogue, filterable. */}
          <Route path="/bistar-parde" element={<Listing />} />

          <Route path="/product/:slug" element={<Product />} />
          <Route path="/build-your-set" element={<SetBuilder />} />
          <Route path="/cart" element={<Cart />} />
          <Route path="/order-placed" element={<OrderPlaced />} />
        </Route>

        {/*  The two screens that must be reachable without a session. Sign-in
            outside the shell, because the shell's sidebar assumes a user.     */}
        <Route path="/shop-panel/sign-in" element={<SignIn />} />
        <Route path="/shop-panel/two-factor-setup" element={<TwoFactorSetup />} />

        <Route
          path="/shop-panel"
          element={<RequireAuth><AdminShell /></RequireAuth>}
        >
          <Route index element={<Overview />} />
          <Route path="orders" element={<Orders />} />
          <Route path="orders/:orderId" element={<OrderDetail />} />
          <Route path="inventory" element={<Inventory />} />
          <Route path="stitching" element={<Stitching />} />
          <Route path="customers" element={<Customers />} />
          <Route path="products" element={<Products />} />
          <Route path="products/new" element={<ProductEditor />} />
          <Route path="products/:productId" element={<ProductEditor />} />
          {/*  Settings decides how the shop takes money; the API enforces the
              same rule, this only avoids offering what would be refused.      */}
          <Route path="settings" element={<RequireAuth ownerOnly><Settings /></RequireAuth>} />
          <Route path="*" element={<PanelNotFound />} />
        </Route>

        {/*  Kept last: a bare slug is a category on the storefront, so it must
            not shadow /shop-panel or the named routes above.                  */}
        <Route element={<Storefront />}>
          <Route path="/:slug" element={<Listing />} />
          <Route path="*" element={<NotFound />} />
        </Route>
      </Routes>
    </>
  );
}

function NotFound() {
  return (
    <main id="main" className="page pb-16 pt-20 text-center">
      <h1 className="display-page mb-3">Not here</h1>
      <p className="mb-6 text-neutral-700">
        That page is not part of the shop. The wedding sets are this way.
      </p>
      <a href="/wedding-sets" className="btn btn-primary btn-lg">See wedding sets</a>
    </main>
  );
}

function PanelNotFound() {
  return (
    <>
      <h1 className="mb-2 text-3xl">No such screen</h1>
      <p className="text-neutral-700">
        That is not one of the panel&rsquo;s screens. Use the menu on the left.
      </p>
    </>
  );
}
