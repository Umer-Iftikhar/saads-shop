import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Products } from './Products';
import type { AdminProduct } from '../../types/admin';

const isOwner = vi.hoisted(() => ({ current: true }));

vi.mock('../../state/auth', () => ({
  useAuth: () => ({
    user: { userId: 'u1', email: 'saad@saadsshop.pk', fullName: 'Saad', roles: ['Owner'] },
    bootstrapping: false, isOwner: isOwner.current,
    signIn: vi.fn(), signOut: vi.fn(), refresh: vi.fn(),
  }),
}));

function aProduct(overrides: Partial<AdminProduct> = {}): AdminProduct {
  return {
    productId: 1,
    name: 'Gulaab Bridal Set',
    slug: 'gulaab-bridal-set',
    categoryId: 1,
    categoryName: 'Wedding sets',
    price: 18_500,
    stitchingDays: 3,
    stock: 6,
    lowStockAt: 3,
    soldCount: 12,
    isActive: true,
    swatchColorValue: '#c67139',
    swatchWeave: 'Woven',
    ...overrides,
  };
}

/** Answers the list with whatever the archived flag asks for, and records writes. */
function anApi(live: AdminProduct[], archivedItems: AdminProduct[]) {
  const calls: { url: string; method: string }[] = [];

  const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
    const method = init?.method ?? 'GET';
    calls.push({ url: String(url), method });

    if (method !== 'GET') return new Response(null, { status: 204 });

    const items = String(url).includes('archivedOnly=true') ? archivedItems : live;

    return new Response(
      JSON.stringify({ items, totalCount: items.length, page: 1, pageSize: 50, totalPages: 1 }),
      { status: 200, headers: { 'Content-Type': 'application/json' } });
  });

  return { fetchMock, calls };
}

function renderProducts() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });

  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter><Products /></MemoryRouter>
    </QueryClientProvider>,
  );
}

const archivedOne = aProduct({
  productId: 2, name: 'Old Chhata', isActive: false,
  deletedAt: '2026-09-01T10:00:00Z', deletedBy: 'Saad',
});

beforeEach(() => { isOwner.current = true; });
afterEach(() => vi.unstubAllGlobals());

describe('the two views', () => {
  it('shows what the shop sells, and does not ask for the archive', async () => {
    const { fetchMock, calls } = anApi([aProduct()], [archivedOne]);
    vi.stubGlobal('fetch', fetchMock);

    renderProducts();
    await screen.findByText('Gulaab Bridal Set');

    expect(calls.every(c => !c.url.includes('archivedOnly'))).toBe(true);
    expect(screen.getByRole('button', { name: 'In the shop' })).toHaveAttribute('aria-pressed', 'true');
  });

  it('switches to the archive and asks the API for it', async () => {
    const { fetchMock, calls } = anApi([aProduct()], [archivedOne]);
    vi.stubGlobal('fetch', fetchMock);

    renderProducts();
    await screen.findByText('Gulaab Bridal Set');

    await userEvent.click(screen.getByRole('button', { name: 'Archived' }));

    await waitFor(() => expect(calls.some(c => c.url.includes('archivedOnly=true'))).toBe(true));
    expect(await screen.findByText('Old Chhata')).toBeInTheDocument();
  });

  it('keeps archived products out of the ordinary list', async () => {
    const { fetchMock } = anApi([aProduct()], [archivedOne]);
    vi.stubGlobal('fetch', fetchMock);

    renderProducts();
    await screen.findByText('Gulaab Bridal Set');

    // A shop that lists what it cannot sell looks bigger than it is.
    expect(screen.queryByText('Old Chhata')).not.toBeInTheDocument();
  });

  it('says when and by whom something was archived', async () => {
    const { fetchMock } = anApi([], [archivedOne]);
    vi.stubGlobal('fetch', fetchMock);

    renderProducts();
    await userEvent.click(screen.getByRole('button', { name: 'Archived' }));

    const row = (await screen.findByText('Old Chhata')).closest('tr')!;
    expect(within(row).getByText(/Saad/)).toBeInTheDocument();
  });

  it('says the archive is empty rather than showing a bare table', async () => {
    const { fetchMock } = anApi([aProduct()], []);
    vi.stubGlobal('fetch', fetchMock);

    renderProducts();
    await userEvent.click(screen.getByRole('button', { name: 'Archived' }));

    expect(await screen.findByText(/Nothing is archived/)).toBeInTheDocument();
  });
});

describe('archiving and bringing back', () => {
  it('archives through DELETE, which the API treats as an archive', async () => {
    const { fetchMock, calls } = anApi([aProduct()], []);
    vi.stubGlobal('fetch', fetchMock);

    renderProducts();
    await screen.findByText('Gulaab Bridal Set');

    await userEvent.click(screen.getByRole('button', { name: 'Archive Gulaab Bridal Set' }));

    await waitFor(() => expect(calls.some(
      c => c.method === 'DELETE' && c.url.endsWith('/admin/products/1'))).toBe(true));
  });

  it('brings one back through its own endpoint, not by undoing a delete', async () => {
    const { fetchMock, calls } = anApi([], [archivedOne]);
    vi.stubGlobal('fetch', fetchMock);

    renderProducts();
    await userEvent.click(screen.getByRole('button', { name: 'Archived' }));
    await screen.findByText('Old Chhata');

    await userEvent.click(screen.getByRole('button', { name: 'Bring Old Chhata back to the shop' }));

    await waitFor(() => expect(calls.some(
      c => c.method === 'POST' && c.url.endsWith('/admin/products/2/restore'))).toBe(true));
  });

  it('names the product in each button, so a column of them is not identical', async () => {
    const { fetchMock } = anApi([aProduct(), aProduct({ productId: 3, name: 'Sage Parde' })], []);
    vi.stubGlobal('fetch', fetchMock);

    renderProducts();
    await screen.findByText('Sage Parde');

    expect(screen.getByRole('button', { name: 'Archive Gulaab Bridal Set' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Archive Sage Parde' })).toBeInTheDocument();
  });

  it('tells the shopkeeper when a restore is refused', async () => {
    //  The name was taken while it was away, or its category closed. Both are
    //  things only the shopkeeper can resolve, so both have to be said.
    const fetchMock = vi.fn(async (_url: string, init?: RequestInit) => {
      if ((init?.method ?? 'GET') === 'POST') {
        return new Response(
          JSON.stringify({ title: 'Another product is using that name now.' }),
          { status: 409, headers: { 'Content-Type': 'application/json' } });
      }
      return new Response(
        JSON.stringify({ items: [archivedOne], totalCount: 1, page: 1, pageSize: 50, totalPages: 1 }),
        { status: 200, headers: { 'Content-Type': 'application/json' } });
    });
    vi.stubGlobal('fetch', fetchMock);

    renderProducts();
    await userEvent.click(screen.getByRole('button', { name: 'Archived' }));
    await screen.findByText('Old Chhata');

    await userEvent.click(screen.getByRole('button', { name: 'Bring Old Chhata back to the shop' }));

    expect(await screen.findByRole('alert'))
      .toHaveTextContent('Another product is using that name now.');
  });

  it('offers neither action to staff, because the API refuses both', async () => {
    isOwner.current = false;
    const { fetchMock } = anApi([aProduct()], []);
    vi.stubGlobal('fetch', fetchMock);

    renderProducts();
    await screen.findByText('Gulaab Bridal Set');

    // Not /^Archive/ — that also matches the "Archived" view toggle, which
    // staff may use.
    expect(screen.queryByRole('button', { name: 'Archive Gulaab Bridal Set' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Archived' })).toBeInTheDocument();
    // Editing is still theirs.
    expect(screen.getByRole('link', { name: 'Edit Gulaab Bridal Set' })).toBeInTheDocument();
  });

  it('does not offer Edit on an archived product, which has no editor', async () => {
    const { fetchMock } = anApi([], [archivedOne]);
    vi.stubGlobal('fetch', fetchMock);

    renderProducts();
    await userEvent.click(screen.getByRole('button', { name: 'Archived' }));
    await screen.findByText('Old Chhata');

    expect(screen.queryByRole('link', { name: /^Edit/ })).not.toBeInTheDocument();
  });
});
