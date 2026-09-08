import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { PhotoField } from './PhotoField';

/** Answers uploads and deletes, and records what was sent. */
function anApi(status = 200) {
  const calls: { url: string; method: string; body: unknown }[] = [];

  const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
    calls.push({ url: String(url), method: init?.method ?? 'GET', body: init?.body });

    if (status !== 200) {
      return new Response(
        JSON.stringify({ title: 'That file is not the kind of image it claims to be.' }),
        { status, headers: { 'Content-Type': 'application/json' } });
    }

    return new Response(
      JSON.stringify({ imagePath: '/media/2026-09/a.webp', thumbnailPath: '/media/2026-09/a-thumb.webp' }),
      { status: 200, headers: { 'Content-Type': 'application/json' } });
  });

  return { fetchMock, calls };
}

function renderField(props: Partial<Parameters<typeof PhotoField>[0]> = {}) {
  const client = new QueryClient({ defaultOptions: { mutations: { retry: false } } });

  return render(
    <QueryClientProvider client={client}>
      <PhotoField productId={1} swatchColorValue="#c67139" swatchWeave="Woven" {...props} />
    </QueryClientProvider>,
  );
}

/** A file of the given name and size. Contents do not matter — the server checks those. */
function aFile(name: string, sizeBytes = 1024) {
  return new File([new Uint8Array(sizeBytes)], name, { type: 'image/jpeg' });
}

const chooser = () => screen.getByLabelText('Choose a product photo');

afterEach(() => vi.unstubAllGlobals());

describe('when there is no photo yet', () => {
  it('draws the cloth and says so', () => {
    vi.stubGlobal('fetch', anApi().fetchMock);

    renderField();

    expect(screen.getByText(/No photo yet/)).toBeInTheDocument();
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
  });

  it('offers to add one', () => {
    vi.stubGlobal('fetch', anApi().fetchMock);

    renderField();

    expect(screen.getByRole('button', { name: 'Add a photo' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Remove' })).not.toBeInTheDocument();
  });

  it('says which types and what size the shop takes, before anything is chosen', () => {
    vi.stubGlobal('fetch', anApi().fetchMock);

    renderField();

    expect(screen.getByText(/JPG, PNG or WEBP, up to 10 MB/)).toBeInTheDocument();
  });
});

describe('when there is one', () => {
  it('shows it, described rather than decorative — the shopkeeper is checking it', () => {
    vi.stubGlobal('fetch', anApi().fetchMock);

    renderField({ imagePath: '/media/2026-09/a.webp' });

    expect(screen.getByRole('img')).toHaveAttribute('src', '/media/2026-09/a.webp');
  });

  it('offers to replace or remove it', () => {
    vi.stubGlobal('fetch', anApi().fetchMock);

    renderField({ imagePath: '/media/2026-09/a.webp' });

    expect(screen.getByRole('button', { name: 'Replace photo' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Remove' })).toBeInTheDocument();
  });

  it('removes it through the API', async () => {
    const { fetchMock, calls } = anApi();
    vi.stubGlobal('fetch', fetchMock);

    renderField({ imagePath: '/media/2026-09/a.webp' });
    await userEvent.click(screen.getByRole('button', { name: 'Remove' }));

    await waitFor(() => expect(calls.some(
      c => c.method === 'DELETE' && c.url.endsWith('/admin/products/1/image'))).toBe(true));
  });
});

describe('uploading', () => {
  it('sends the file as multipart, letting the browser set the boundary', async () => {
    const { fetchMock, calls } = anApi();
    vi.stubGlobal('fetch', fetchMock);

    renderField();
    await userEvent.upload(chooser(), aFile('cloth.jpg'));

    await waitFor(() => expect(calls.some(c => c.method === 'POST')).toBe(true));

    const post = calls.find(c => c.method === 'POST')!;
    expect(post.url).toBe('/api/admin/products/1/image');
    // FormData, not JSON: a stringified File is "[object File]".
    expect(post.body).toBeInstanceOf(FormData);
  });

  it('sends the file under the name the API binds', async () => {
    const { fetchMock, calls } = anApi();
    vi.stubGlobal('fetch', fetchMock);

    renderField();
    await userEvent.upload(chooser(), aFile('cloth.jpg'));

    await waitFor(() => expect(calls.some(c => c.method === 'POST')).toBe(true));

    const form = calls.find(c => c.method === 'POST')!.body as FormData;
    expect((form.get('file') as File).name).toBe('cloth.jpg');
  });

  it('offers the file picker only the types the shop takes', () => {
    vi.stubGlobal('fetch', anApi().fetchMock);

    renderField();

    expect(chooser()).toHaveAttribute('accept', '.jpg,.jpeg,.png,.webp');
  });
});

describe('what it refuses before spending the shop\'s bandwidth', () => {
  it('refuses a file type without uploading it', async () => {
    //  The server checks the bytes and would refuse this anyway. The point of
    //  checking here is not to make a shopkeeper wait for the round trip.
    const { fetchMock, calls } = anApi();
    vi.stubGlobal('fetch', fetchMock);

    renderField();

    //  applyAccept: false because the accept attribute is not the check under
    //  test. A file picker set to "All files" ignores it, and so does a
    //  drag-and-drop, which is exactly when this guard has to hold.
    await userEvent.upload(chooser(), aFile('payload.exe'), { applyAccept: false });

    expect(await screen.findByRole('alert')).toHaveTextContent(/JPG, PNG or WEBP/);
    expect(calls.filter(c => c.method === 'POST')).toHaveLength(0);
  });

  it('refuses an oversized photo without uploading it', async () => {
    const { fetchMock, calls } = anApi();
    vi.stubGlobal('fetch', fetchMock);

    renderField();
    await userEvent.upload(chooser(), aFile('huge.jpg', 11 * 1024 * 1024));

    expect(await screen.findByRole('alert')).toHaveTextContent(/11 MB/);
    expect(calls.filter(c => c.method === 'POST')).toHaveLength(0);
  });

  it('still shows what the server refused, since the browser cannot see the bytes', async () => {
    const { fetchMock } = anApi(400);
    vi.stubGlobal('fetch', fetchMock);

    renderField();
    await userEvent.upload(chooser(), aFile('renamed.jpg'));

    expect(await screen.findByRole('alert'))
      .toHaveTextContent('That file is not the kind of image it claims to be.');
  });

  it('lets the same file be chosen again after a failure', async () => {
    //  The input keeps its value, so without clearing it, picking the same file
    //  a second time fires no change event and looks like nothing happened.
    const { fetchMock, calls } = anApi(400);
    vi.stubGlobal('fetch', fetchMock);

    renderField();
    await userEvent.upload(chooser(), aFile('cloth.jpg'));
    await screen.findByRole('alert');

    await userEvent.upload(chooser(), aFile('cloth.jpg'));

    await waitFor(() => expect(calls.filter(c => c.method === 'POST')).toHaveLength(2));
  });
});
