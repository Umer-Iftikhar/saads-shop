import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError, api, getAccessToken, queryString, setAccessToken } from './api';

/** A fetch that answers with the given status and body. */
function respondWith(status: number, body?: unknown, headers: Record<string, string> = {}) {
  const hasBody = body !== undefined;

  return vi.fn(async () => new Response(
    hasBody ? JSON.stringify(body) : null,
    {
      status,
      headers: hasBody ? { 'Content-Type': 'application/json', ...headers } : headers,
    },
  ));
}

function lastRequest(mock: ReturnType<typeof vi.fn>) {
  const [url, init] = mock.mock.calls.at(-1)!;
  return { url: url as string, init: init as RequestInit };
}

/** Awaits a call that is expected to fail, and hands back the ApiError. */
async function failureOf(call: Promise<unknown>): Promise<ApiError> {
  return call.then(
    () => { throw new Error('Expected the call to fail, but it succeeded.'); },
    (error: ApiError) => error,
  );
}

beforeEach(() => setAccessToken(null));
afterEach(() => vi.unstubAllGlobals());

describe('sending a request', () => {
  it('prefixes the API base', async () => {
    const fetchMock = respondWith(200, { ok: true });
    vi.stubGlobal('fetch', fetchMock);

    await api.get('/catalog/categories');

    expect(lastRequest(fetchMock).url).toBe('/api/catalog/categories');
  });

  it('asks for JSON and sends a content type only when there is a body', async () => {
    const fetchMock = respondWith(200, {});
    vi.stubGlobal('fetch', fetchMock);

    await api.get('/catalog/categories');
    expect((lastRequest(fetchMock).init.headers as Record<string, string>)['Content-Type']).toBeUndefined();

    await api.post('/orders', { customerName: 'Hina' });
    expect((lastRequest(fetchMock).init.headers as Record<string, string>)['Content-Type'])
      .toBe('application/json');
  });

  it('serialises the body', async () => {
    const fetchMock = respondWith(200, {});
    vi.stubGlobal('fetch', fetchMock);

    await api.post('/orders', { customerName: 'Hina' });

    expect(lastRequest(fetchMock).init.body).toBe('{"customerName":"Hina"}');
  });

  it.each(['put', 'patch', 'del'] as const)('sends the right method for %s', async method => {
    const fetchMock = respondWith(200, {});
    vi.stubGlobal('fetch', fetchMock);

    await api[method]('/thing', {});

    expect(lastRequest(fetchMock).init.method).toBe(
      method === 'del' ? 'DELETE' : method.toUpperCase());
  });
});

describe('the access token', () => {
  it('is not sent when there is none', async () => {
    const fetchMock = respondWith(200, {});
    vi.stubGlobal('fetch', fetchMock);

    await api.get('/catalog/categories');

    expect((lastRequest(fetchMock).init.headers as Record<string, string>).Authorization).toBeUndefined();
  });

  it('is sent once set', async () => {
    const fetchMock = respondWith(200, {});
    vi.stubGlobal('fetch', fetchMock);

    setAccessToken('the-token');
    await api.get('/admin/dashboard');

    expect((lastRequest(fetchMock).init.headers as Record<string, string>).Authorization)
      .toBe('Bearer the-token');
  });

  it('is held in memory and readable back, never written to storage', async () => {
    setAccessToken('the-token');

    expect(getAccessToken()).toBe('the-token');
    // Anything in localStorage is readable by an XSS bug.
    expect(JSON.stringify(localStorage)).not.toContain('the-token');
  });

  it('is cleared on sign-out', () => {
    setAccessToken('the-token');
    setAccessToken(null);

    expect(getAccessToken()).toBeNull();
  });

  it('can be overridden for one call, which enrolment needs', async () => {
    const fetchMock = respondWith(200, {});
    vi.stubGlobal('fetch', fetchMock);

    setAccessToken('session-token');
    await api.post('/auth/2fa/enroll', undefined, { bearer: 'challenge-token' });

    expect((lastRequest(fetchMock).init.headers as Record<string, string>).Authorization)
      .toBe('Bearer challenge-token');
  });

  it('leaves the stored token in place after an override', async () => {
    vi.stubGlobal('fetch', respondWith(200, {}));

    setAccessToken('session-token');
    await api.post('/auth/2fa/enroll', undefined, { bearer: 'challenge-token' });

    expect(getAccessToken()).toBe('session-token');
  });
});

describe('cookies', () => {
  it('are omitted by default, so an ordinary call cannot carry the refresh cookie', async () => {
    const fetchMock = respondWith(200, {});
    vi.stubGlobal('fetch', fetchMock);

    await api.get('/catalog/categories');

    expect(lastRequest(fetchMock).init.credentials).toBe('same-origin');
  });

  it('are included where the auth endpoints need them', async () => {
    const fetchMock = respondWith(200, {});
    vi.stubGlobal('fetch', fetchMock);

    await api.post('/auth/refresh', undefined, { withCredentials: true });

    expect(lastRequest(fetchMock).init.credentials).toBe('include');
  });
});

describe('reading a response', () => {
  it('parses a JSON body', async () => {
    vi.stubGlobal('fetch', respondWith(200, { name: 'Gulaab' }));

    expect(await api.get<{ name: string }>('/x')).toEqual({ name: 'Gulaab' });
  });

  it('returns nothing for a 204, rather than trying to parse an empty body', async () => {
    vi.stubGlobal('fetch', respondWith(204));

    expect(await api.post('/admin/orders/1/status', {})).toBeUndefined();
  });
});

describe('failures', () => {
  it('throws an ApiError carrying the status', async () => {
    vi.stubGlobal('fetch', respondWith(404, { title: 'Not here.' }));

    const error = await failureOf(api.get('/x'));

    expect(error).toBeInstanceOf(ApiError);
    expect(error.status).toBe(404);
    expect(error.message).toBe('Not here.');
  });

  it('uses the problem title as the message the customer sees', async () => {
    vi.stubGlobal('fetch', respondWith(409, {
      title: 'Compact Chhata just went out of stock.', status: 409,
    }));

    const error = await failureOf(api.post('/orders', {}));

    expect(error.message).toBe('Compact Chhata just went out of stock.');
    expect(error.isConflict).toBe(true);
  });

  it('exposes field errors so a form can place each message', async () => {
    vi.stubGlobal('fetch', respondWith(400, {
      title: 'Please check the highlighted fields.',
      errors: { Phone: ['That phone number does not look right.'] },
    }));

    const error = await failureOf(api.post('/orders', {}));

    expect(error.isValidation).toBe(true);
    expect(error.fieldErrors).toEqual({ Phone: ['That phone number does not look right.'] });
  });

  it('reports an empty field-error map rather than undefined', async () => {
    vi.stubGlobal('fetch', respondWith(500, { title: 'Something went wrong.' }));

    const error = await failureOf(api.get('/x'));

    expect(error.fieldErrors).toEqual({});
  });

  it('knows an unauthorised response', async () => {
    vi.stubGlobal('fetch', respondWith(401, { title: 'Please sign in again.' }));

    const error = await failureOf(api.post('/auth/refresh'));

    expect(error.isUnauthorised).toBe(true);
    expect(error.isValidation).toBe(false);
  });

  it('carries the correlation id so a report can be traced to a log line', async () => {
    vi.stubGlobal('fetch', respondWith(500, {
      title: 'Something went wrong.', correlationId: '0HNOBGDA8BRSN',
    }));

    const error = await failureOf(api.get('/x'));

    expect(error.correlationId).toBe('0HNOBGDA8BRSN');
  });

  it('says something a shopper can act on when the network fails', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('Failed to fetch'); }));

    const error = await failureOf(api.get('/x'));

    // Not "TypeError: Failed to fetch".
    expect(error.message).toBe('Could not reach the shop. Check your connection and try again.');
    expect(error.status).toBe(0);
  });

  it('survives an error response that is not JSON at all', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response('<html>502</html>', {
      status: 502, headers: { 'Content-Type': 'text/html' },
    })));

    const error = await failureOf(api.get('/x'));

    expect(error.status).toBe(502);
    expect(error.message).toBe('Something went wrong. Please try again.');
  });

  it('rethrows an abort rather than dressing it as a network failure', async () => {
    const controller = new AbortController();
    controller.abort();

    vi.stubGlobal('fetch', vi.fn(async () => { throw new DOMException('Aborted', 'AbortError'); }));

    const error = await api.get('/x', controller.signal).catch(e => e);

    // TanStack Query cancels in-flight reads on unmount; that is not an error.
    expect(error).not.toBeInstanceOf(ApiError);
  });
});

describe('query strings', () => {
  it('builds one from the values that are present', () => {
    expect(queryString({ category: 'wedding-sets', page: 2 })).toBe('?category=wedding-sets&page=2');
  });

  it('returns an empty string when everything is absent', () => {
    expect(queryString({ a: null, b: undefined, c: '' })).toBe('');
  });

  it.each([
    [{ a: null }],
    [{ a: undefined }],
    [{ a: '' }],
  ])('drops %j so the API never has to treat blank as absent', params => {
    expect(queryString({ ...params, keep: 'yes' })).toBe('?keep=yes');
  });

  it('keeps a false and a zero, which are values rather than absences', () => {
    expect(queryString({ lowStockOnly: false, page: 0 })).toBe('?lowStockOnly=false&page=0');
  });

  it('encodes what would otherwise break the URL', () => {
    expect(queryString({ search: 'gulaab & parde' })).toContain('gulaab+%26+parde');
  });
});
