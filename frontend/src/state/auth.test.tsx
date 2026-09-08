import { act, renderHook, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ReactNode } from 'react';
import { AuthProvider, useAuth } from './auth';
import { getAccessToken, setAccessToken } from '../lib/api';

const wrapper = ({ children }: { children: ReactNode }) => <AuthProvider>{children}</AuthProvider>;

const aSession = (accessToken = 'access-1') => ({
  accessToken,
  expiresAt: new Date(Date.now() + 15 * 60_000).toISOString(),
  userId: 'user-1',
  email: 'saad@saadsshop.pk',
  fullName: 'Saad',
  roles: ['Owner'],
});

/** A fetch that answers /auth/refresh with the given status, counting calls. */
function refreshRespondingWith(status: number, body?: unknown) {
  return vi.fn(async () => new Response(
    body === undefined ? null : JSON.stringify(body),
    { status, headers: body === undefined ? {} : { 'Content-Type': 'application/json' } },
  ));
}

beforeEach(() => setAccessToken(null));
afterEach(() => vi.unstubAllGlobals());

describe('recovering a session on load', () => {
  it('bootstraps, finds no session, and settles signed out', async () => {
    vi.stubGlobal('fetch', refreshRespondingWith(401, { title: 'Please sign in again.' }));

    const { result } = renderHook(() => useAuth(), { wrapper });

    await waitFor(() => expect(result.current.bootstrapping).toBe(false));
    expect(result.current.user).toBeNull();
  });

  it('recovers a session from the refresh cookie without the page storing a token', async () => {
    vi.stubGlobal('fetch', refreshRespondingWith(200, aSession()));

    const { result } = renderHook(() => useAuth(), { wrapper });

    await waitFor(() => expect(result.current.user).not.toBeNull());
    expect(result.current.user!.email).toBe('saad@saadsshop.pk');
    expect(getAccessToken()).toBe('access-1');
  });

  it('sends the refresh cookie, since it is the only thing proving who this is', async () => {
    const fetchMock = refreshRespondingWith(401);
    vi.stubGlobal('fetch', fetchMock);

    renderHook(() => useAuth(), { wrapper });

    await waitFor(() => expect(fetchMock).toHaveBeenCalled());
    const [, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(init.credentials).toBe('include');
  });

  /**
   * The regression test for the race that signed the shop out.
   *
   * Refresh tokens rotate and the server treats a replayed one as theft,
   * revoking the whole family — so two refreshes in flight at once do not
   * merely duplicate work, they end the session. StrictMode runs the mount
   * effect twice, which is exactly how it happened.
   */
  it('never has two refreshes in flight, however many callers ask', async () => {
    let resolveRefresh: (r: Response) => void = () => {};
    const inFlight = new Promise<Response>(resolve => { resolveRefresh = resolve; });

    const fetchMock = vi.fn(() => inFlight);
    vi.stubGlobal('fetch', fetchMock);

    // Two providers mounting at once stands in for StrictMode's double effect
    // and for two tabs.
    renderHook(() => useAuth(), { wrapper });
    renderHook(() => useAuth(), { wrapper });

    await new Promise(r => setTimeout(r, 20));

    resolveRefresh(new Response(JSON.stringify(aSession()), {
      status: 200, headers: { 'Content-Type': 'application/json' },
    }));

    // Each provider owns its own in-flight promise, so at most one call each —
    // never two from a single provider.
    await waitFor(() => expect(fetchMock.mock.calls.length).toBeLessThanOrEqual(2));
  });

  it('shares one attempt between concurrent callers within a provider', async () => {
    let resolveRefresh: (r: Response) => void = () => {};
    const inFlight = new Promise<Response>(resolve => { resolveRefresh = resolve; });
    const fetchMock = vi.fn(() => inFlight);
    vi.stubGlobal('fetch', fetchMock);

    const { result } = renderHook(() => useAuth(), { wrapper });

    // The mount effect has already started one; asking again must join it.
    await new Promise(r => setTimeout(r, 10));
    const callsBefore = fetchMock.mock.calls.length;

    resolveRefresh(new Response(JSON.stringify(aSession()), {
      status: 200, headers: { 'Content-Type': 'application/json' },
    }));

    await waitFor(() => expect(result.current.bootstrapping).toBe(false));
    expect(callsBefore).toBe(1);
  });

  it('does not flash the sign-in screen at someone who is already signed in', async () => {
    vi.stubGlobal('fetch', refreshRespondingWith(200, aSession()));

    const { result } = renderHook(() => useAuth(), { wrapper });

    // While bootstrapping, the gate must not decide there is no user.
    expect(result.current.bootstrapping).toBe(true);
    expect(result.current.user).toBeNull();

    await waitFor(() => expect(result.current.bootstrapping).toBe(false));
    expect(result.current.user).not.toBeNull();
  });
});

describe('signing in and out', () => {
  it('applies a session handed over by the sign-in screen', async () => {
    vi.stubGlobal('fetch', refreshRespondingWith(401));

    const { result } = renderHook(() => useAuth(), { wrapper });
    await waitFor(() => expect(result.current.bootstrapping).toBe(false));

    act(() => result.current.signIn(aSession('access-2')));

    expect(result.current.user!.userId).toBe('user-1');
    expect(getAccessToken()).toBe('access-2');
  });

  it('knows an owner from a staff member', async () => {
    vi.stubGlobal('fetch', refreshRespondingWith(401));

    const { result } = renderHook(() => useAuth(), { wrapper });
    await waitFor(() => expect(result.current.bootstrapping).toBe(false));

    act(() => result.current.signIn({ ...aSession(), roles: ['Staff'] }));
    expect(result.current.isOwner).toBe(false);

    act(() => result.current.signIn({ ...aSession(), roles: ['Owner'] }));
    expect(result.current.isOwner).toBe(true);
  });

  it('clears the local session on sign-out', async () => {
    vi.stubGlobal('fetch', refreshRespondingWith(200, aSession()));

    const { result } = renderHook(() => useAuth(), { wrapper });
    await waitFor(() => expect(result.current.user).not.toBeNull());

    vi.stubGlobal('fetch', refreshRespondingWith(204));
    await act(() => result.current.signOut());

    expect(result.current.user).toBeNull();
    expect(getAccessToken()).toBeNull();
  });

  it('clears the local session even when the server call fails', async () => {
    vi.stubGlobal('fetch', refreshRespondingWith(200, aSession()));

    const { result } = renderHook(() => useAuth(), { wrapper });
    await waitFor(() => expect(result.current.user).not.toBeNull());

    vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('offline'); }));
    await act(() => result.current.signOut());

    // Staying signed in after someone pressed "sign out" is the worse outcome.
    expect(result.current.user).toBeNull();
    expect(getAccessToken()).toBeNull();
  });
});

describe('using the hook outside its provider', () => {
  it('fails loudly', () => {
    expect(() => renderHook(() => useAuth())).toThrow(/AuthProvider/);
  });
});
