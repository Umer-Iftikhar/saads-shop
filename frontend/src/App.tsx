import { Route, Routes, useLocation } from 'react-router-dom';
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

export default function App() {
  const settings = useShopSettings();

  return (
    <>
      <a className="skip-link" href="#main">Skip to content</a>
      <FocusOnNavigate />

      <ShopNav banner={settings.data?.bannerText} />

      <Routes>
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
        <Route path="/:slug" element={<Listing />} />

        <Route path="/product/:slug" element={<Product />} />
        <Route path="/build-your-set" element={<SetBuilder />} />
        <Route path="/cart" element={<Cart />} />
        <Route path="/order-placed" element={<OrderPlaced />} />

        <Route path="*" element={<NotFound />} />
      </Routes>

      <ShopFooter settings={settings.data} />
    </>
  );
}

function NotFound() {
  return (
    <main id="main" className="page page-pad" style={{ paddingBlock: '80px 64px', textAlign: 'center' }}>
      <h1 className="display-page" style={{ marginBottom: 12 }}>Not here</h1>
      <p style={{ color: 'var(--color-neutral-700)', marginBottom: 24 }}>
        That page is not part of the shop. The wedding sets are this way.
      </p>
      <a href="/wedding-sets" className="btn btn-primary btn-lg">See wedding sets</a>
    </main>
  );
}
