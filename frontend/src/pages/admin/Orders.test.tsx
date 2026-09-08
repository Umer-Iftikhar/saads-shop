import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Orders } from './Orders';
import type { OrderList, OrderSummary } from '../../types/admin';

function anOrder(overrides: Partial<OrderSummary> = {}): OrderSummary {
  return {
    orderId: 1,
    reference: 'SS-2419',
    placedAt: '2026-09-04T10:00:00Z',
    customerName: 'Hina Tariq',
    phone: '03012345678',
    itemSummary: 'Gulaab Bridal Set ×1',
    lineCount: 1,
    total: 18_500,
    paymentMethod: 'CashOnDelivery',
    status: 'Placed',
    ...overrides,
  };
}

function aPage(items: OrderSummary[] = [anOrder()], overrides: Partial<OrderList> = {}): OrderList {
  return {
    items,
    totalCount: items.length,
    needsAttentionCount: 0,
    page: 1,
    pageSize: 25,
    totalPages: 1,
    ...overrides,
  };
}

/** Answers every read with the same page, and records the URLs asked for. */
function serving(body: OrderList | { fail: true }) {
  return vi.fn(async (url: string) => {
    if ('fail' in body) {
      return new Response(JSON.stringify({ title: 'Something went wrong.' }), {
        status: 500, headers: { 'Content-Type': 'application/json' },
      });
    }
    void url;
    return new Response(JSON.stringify(body), {
      status: 200, headers: { 'Content-Type': 'application/json' },
    });
  });
}

/** The last URL the page actually asked the API for. */
function lastUrl(fetchMock: ReturnType<typeof vi.fn>) {
  return String(fetchMock.mock.calls.at(-1)![0]);
}

let currentSearch = '';

function ShowsLocation() {
  currentSearch = useLocation().search;
  return null;
}

function renderOrders(initialEntry = '/shop-panel/orders') {
  //  Retries off: an error test that waits three exponential backoffs is a test
  //  that times out for reasons unrelated to the page.
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[initialEntry]}>
        <Routes>
          <Route path="/shop-panel/orders" element={<><Orders /><ShowsLocation /></>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

const from = () => screen.getByLabelText('From');
const to   = () => screen.getByLabelText('To');

beforeEach(() => { currentSearch = ''; });
afterEach(() => vi.unstubAllGlobals());

describe('the date filter', () => {
  /*
   * The same three rules the server's [DateRange] attribute applies and the
   * stored procedure checks again. This layer exists so a shopkeeper who types
   * the dates backwards is told at once instead of after a round trip — so
   * every test here asserts both the message and that nothing was sent.
   */

  it('refuses a range that starts after it ends, and sends nothing', async () => {
    const fetchMock = serving(aPage());
    vi.stubGlobal('fetch', fetchMock);

    renderOrders();
    await waitFor(() => expect(screen.getByText('SS-2419')).toBeInTheDocument());
    const callsBefore = fetchMock.mock.calls.length;

    await userEvent.type(from(), '2026-09-04');
    await userEvent.type(to(), '2026-09-01');
    await userEvent.click(screen.getByRole('button', { name: 'Apply' }));

    expect(screen.getByText(/before/i)).toBeInTheDocument();
    expect(fetchMock.mock.calls.length).toBe(callsBefore);
    expect(currentSearch).toBe('');
  });

  it('marks the offending input invalid and points it at the message', async () => {
    vi.stubGlobal('fetch', serving(aPage()));

    renderOrders();
    await waitFor(() => expect(screen.getByText('SS-2419')).toBeInTheDocument());

    await userEvent.type(from(), '2026-09-04');
    await userEvent.type(to(), '2026-09-01');
    await userEvent.click(screen.getByRole('button', { name: 'Apply' }));

    // The message sits on the start date, which is the one to move.
    expect(from()).toHaveAttribute('aria-invalid', 'true');
    expect(from()).toHaveAccessibleDescription(/on or before the end date/i);
  });

  it('refuses a date in the future', async () => {
    vi.stubGlobal('fetch', serving(aPage()));

    renderOrders();
    await waitFor(() => expect(screen.getByText('SS-2419')).toBeInTheDocument());

    const nextYear = new Date();
    nextYear.setFullYear(nextYear.getFullYear() + 1);
    await userEvent.type(from(), nextYear.toISOString().slice(0, 10));
    await userEvent.click(screen.getByRole('button', { name: 'Apply' }));

    expect(screen.getByText(/future/i)).toBeInTheDocument();
    expect(currentSearch).toBe('');
  });

  it('refuses a span longer than a year, which is what the report can hold', async () => {
    vi.stubGlobal('fetch', serving(aPage()));

    renderOrders();
    await waitFor(() => expect(screen.getByText('SS-2419')).toBeInTheDocument());

    await userEvent.type(from(), '2020-01-01');
    await userEvent.type(to(), '2024-01-01');
    await userEvent.click(screen.getByRole('button', { name: 'Apply' }));

    // 366 days is the cap, and the message says how far over the range went.
    expect(screen.getByText(/1,462 days.*366 days or fewer/)).toBeInTheDocument();
    expect(currentSearch).toBe('');
  });

  it('accepts an open range, because "everything since Eid" is a real question', async () => {
    vi.stubGlobal('fetch', serving(aPage()));

    renderOrders();
    await waitFor(() => expect(screen.getByText('SS-2419')).toBeInTheDocument());

    await userEvent.type(from(), '2026-03-20');
    await userEvent.click(screen.getByRole('button', { name: 'Apply' }));

    await waitFor(() => expect(currentSearch).toContain('fromDate=2026-03-20'));
    expect(screen.queryByText(/before|future|year/i)).not.toBeInTheDocument();
  });

  it('puts a valid range in the URL, so the filter can be shared and reloaded', async () => {
    const fetchMock = serving(aPage());
    vi.stubGlobal('fetch', fetchMock);

    renderOrders();
    await waitFor(() => expect(screen.getByText('SS-2419')).toBeInTheDocument());

    await userEvent.type(from(), '2026-09-01');
    await userEvent.type(to(), '2026-09-04');
    await userEvent.click(screen.getByRole('button', { name: 'Apply' }));

    await waitFor(() => expect(currentSearch).toContain('fromDate=2026-09-01'));
    expect(currentSearch).toContain('toDate=2026-09-04');
    await waitFor(() => expect(lastUrl(fetchMock)).toContain('fromDate=2026-09-01'));
  });

  it('clears the message once the range is fixed', async () => {
    vi.stubGlobal('fetch', serving(aPage()));

    renderOrders();
    await waitFor(() => expect(screen.getByText('SS-2419')).toBeInTheDocument());

    await userEvent.type(from(), '2026-09-04');
    await userEvent.type(to(), '2026-09-01');
    await userEvent.click(screen.getByRole('button', { name: 'Apply' }));
    expect(screen.getByText(/before/i)).toBeInTheDocument();

    await userEvent.clear(to());
    await userEvent.type(to(), '2026-09-06');
    await userEvent.click(screen.getByRole('button', { name: 'Apply' }));

    await waitFor(() => expect(screen.queryByText(/before/i)).not.toBeInTheDocument());
  });

  it('offers no future date in the picker itself', async () => {
    vi.stubGlobal('fetch', serving(aPage()));

    renderOrders();
    await waitFor(() => expect(screen.getByText('SS-2419')).toBeInTheDocument());

    const today = new Date().toISOString().slice(0, 10);
    expect(from()).toHaveAttribute('max', today);
    expect(to()).toHaveAttribute('max', today);
  });

  it('reads the range back out of the URL on a reload', async () => {
    vi.stubGlobal('fetch', serving(aPage()));

    renderOrders('/shop-panel/orders?fromDate=2026-09-01&toDate=2026-09-04');
    await waitFor(() => expect(screen.getByText('SS-2419')).toBeInTheDocument());

    expect(from()).toHaveValue('2026-09-01');
    expect(to()).toHaveValue('2026-09-04');
  });
});

describe('searching and filtering', () => {
  it('sends the search text', async () => {
    const fetchMock = serving(aPage());
    vi.stubGlobal('fetch', fetchMock);

    renderOrders();
    await waitFor(() => expect(screen.getByText('SS-2419')).toBeInTheDocument());

    await userEvent.type(screen.getByRole('searchbox'), 'Hina');
    await userEvent.click(screen.getByRole('button', { name: 'Apply' }));

    await waitFor(() => expect(lastUrl(fetchMock)).toContain('search=Hina'));
  });

  it('filters by status and shows which chip is on', async () => {
    const fetchMock = serving(aPage());
    vi.stubGlobal('fetch', fetchMock);

    renderOrders();
    await waitFor(() => expect(screen.getByText('SS-2419')).toBeInTheDocument());

    await userEvent.click(screen.getByRole('button', { name: 'Stitching' }));

    await waitFor(() => expect(lastUrl(fetchMock)).toContain('status=Stitching'));
    expect(screen.getByRole('button', { name: 'Stitching' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('button', { name: 'All' })).toHaveAttribute('aria-pressed', 'false');
  });

  it('goes back to page one when the filter changes, so page 3 of nothing cannot happen', async () => {
    const fetchMock = serving(aPage());
    vi.stubGlobal('fetch', fetchMock);

    renderOrders('/shop-panel/orders?page=3');
    await waitFor(() => expect(screen.getByText('SS-2419')).toBeInTheDocument());

    await userEvent.click(screen.getByRole('button', { name: 'Ready' }));

    await waitFor(() => expect(currentSearch).not.toContain('page=3'));
  });

  it('clears every filter at once', async () => {
    vi.stubGlobal('fetch', serving(aPage()));

    renderOrders('/shop-panel/orders?status=Ready&search=Hina&fromDate=2026-09-01');
    await waitFor(() => expect(screen.getByText('SS-2419')).toBeInTheDocument());

    await userEvent.click(screen.getByRole('button', { name: 'Clear' }));

    await waitFor(() => expect(currentSearch).toBe(''));
    expect(from()).toHaveValue('');
    expect(screen.getByRole('searchbox')).toHaveValue('');
  });

  it('offers nothing to clear when nothing is filtered', async () => {
    vi.stubGlobal('fetch', serving(aPage()));

    renderOrders();
    await waitFor(() => expect(screen.getByText('SS-2419')).toBeInTheDocument());

    expect(screen.queryByRole('button', { name: 'Clear' })).not.toBeInTheDocument();
  });
});

describe('the table', () => {
  it('shows an order the way the shop reads it', async () => {
    vi.stubGlobal('fetch', serving(aPage()));

    renderOrders();

    const row = (await screen.findByText('SS-2419')).closest('tr')!;
    expect(within(row).getByText('Hina Tariq')).toBeInTheDocument();
    expect(within(row).getByText('Rs 18,500')).toBeInTheDocument();
    // "CashOnDelivery" is a database value, not something to show a person.
    expect(within(row).getByText('Cash On Delivery')).toBeInTheDocument();
    expect(within(row).getByText('Placed')).toBeInTheDocument();
  });

  it('names each open link after its order, not "Open" seven times', async () => {
    vi.stubGlobal('fetch', serving(aPage([
      anOrder(),
      anOrder({ orderId: 2, reference: 'SS-2420', customerName: 'Ayesha' }),
    ])));

    renderOrders();

    expect(await screen.findByRole('link', { name: 'Open SS-2419' }))
      .toHaveAttribute('href', '/shop-panel/orders/1');
    expect(screen.getByRole('link', { name: 'Open SS-2420' }))
      .toHaveAttribute('href', '/shop-panel/orders/2');
  });

  it('counts the orders, and says how many need attention', async () => {
    vi.stubGlobal('fetch', serving(aPage([anOrder(), anOrder({ orderId: 2 })], {
      totalCount: 2, needsAttentionCount: 3,
    })));

    renderOrders();

    expect(await screen.findByText(/2 orders/)).toBeInTheDocument();
    expect(screen.getByText(/3 need attention/)).toBeInTheDocument();
  });

  it('says "1 order", not "1 orders"', async () => {
    vi.stubGlobal('fetch', serving(aPage([anOrder()], { totalCount: 1 })));

    renderOrders();

    expect(await screen.findByText(/1 order$/)).toBeInTheDocument();
  });

  it('says so plainly when the filters match nothing', async () => {
    vi.stubGlobal('fetch', serving(aPage([], { totalCount: 0 })));

    renderOrders();

    expect(await screen.findByText('No orders match those filters.')).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('offers a retry when the read fails', async () => {
    vi.stubGlobal('fetch', serving({ fail: true }));

    renderOrders();

    expect(await screen.findByRole('alert')).toHaveTextContent('Could not load orders');
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument();
  });

  it('announces the wait while the first page loads', () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(() => {})));

    renderOrders();

    expect(screen.getByRole('status')).toHaveTextContent('Loading orders…');
  });
});

describe('paging', () => {
  const twoPages = () => aPage([anOrder()], { totalCount: 40, page: 1, totalPages: 2 });

  it('hides the pager when everything fits on one page', async () => {
    vi.stubGlobal('fetch', serving(aPage()));

    renderOrders();
    await screen.findByText('SS-2419');

    expect(screen.queryByRole('navigation', { name: 'Pages' })).not.toBeInTheDocument();
  });

  it('moves to the next page', async () => {
    vi.stubGlobal('fetch', serving(twoPages()));

    renderOrders();
    await screen.findByText('SS-2419');

    await userEvent.click(screen.getByRole('button', { name: 'Next →' }));

    await waitFor(() => expect(currentSearch).toContain('page=2'));
  });

  it('cannot go back from the first page', async () => {
    vi.stubGlobal('fetch', serving(twoPages()));

    renderOrders();
    await screen.findByText('SS-2419');

    expect(screen.getByRole('button', { name: '← Previous' })).toBeDisabled();
  });

  it('cannot go past the last page', async () => {
    vi.stubGlobal('fetch', serving(aPage([anOrder()], { totalCount: 40, page: 2, totalPages: 2 })));

    renderOrders('/shop-panel/orders?page=2');
    await screen.findByText('SS-2419');

    expect(screen.getByRole('button', { name: 'Next →' })).toBeDisabled();
    expect(screen.getByText('Page 2 of 2')).toBeInTheDocument();
  });
});
