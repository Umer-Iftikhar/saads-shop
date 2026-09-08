import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SignIn } from './SignIn';

const signIn = vi.hoisted(() => vi.fn());

vi.mock('../../state/auth', () => ({
  useAuth: () => ({
    user: null, bootstrapping: false, isOwner: false,
    signIn, signOut: vi.fn(), refresh: vi.fn(),
  }),
}));

const aSession = {
  accessToken: 'access-1',
  expiresAt: new Date(Date.now() + 15 * 60_000).toISOString(),
  userId: 'u1', email: 'saad@saadsshop.pk', fullName: 'Saad', roles: ['Owner'],
};

type Reply = { status: number; body?: unknown };

/**
 * Answers each endpoint from a script, and records what was sent.
 *
 * Sign-in is two calls, not one — `/auth/login` returns a challenge and
 * `/auth/2fa/verify` exchanges it for a session — so a single canned response
 * cannot express the flow.
 */
function apiServing(routes: Record<string, Reply | Reply[]>) {
  const remaining = { ...routes };

  return vi.fn(async (url: string, init?: RequestInit) => {
    const path = Object.keys(remaining).find(key => String(url).includes(key));
    if (!path) throw new Error(`No stub for ${url}`);

    const scripted = remaining[path];
    const reply = Array.isArray(scripted) ? (scripted.shift() ?? { status: 500 }) : scripted;
    void init;

    return new Response(reply.body === undefined ? null : JSON.stringify(reply.body), {
      status: reply.status,
      headers: reply.body === undefined ? {} : { 'Content-Type': 'application/json' },
    });
  });
}

function bodyOf(fetchMock: ReturnType<typeof vi.fn>, path: string) {
  const call = fetchMock.mock.calls.find(([url]) => String(url).includes(path));
  return call ? JSON.parse((call[1] as RequestInit).body as string) : null;
}

function initOf(fetchMock: ReturnType<typeof vi.fn>, path: string) {
  const call = fetchMock.mock.calls.find(([url]) => String(url).includes(path));
  return call?.[1] as RequestInit | undefined;
}

let landedOn = '';
let landedState: unknown = null;

function Landed({ name }: { name: string }) {
  const location = useLocation();
  landedOn = name;
  landedState = location.state;
  return <h1>{name}</h1>;
}

function renderSignIn(state?: { from?: string }) {
  const client = new QueryClient({ defaultOptions: { mutations: { retry: false } } });

  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[{ pathname: '/shop-panel/sign-in', state }]}>
        <Routes>
          <Route path="/shop-panel/sign-in" element={<SignIn />} />
          <Route path="/shop-panel" element={<Landed name="Overview" />} />
          <Route path="/shop-panel/orders" element={<Landed name="Orders" />} />
          <Route path="/shop-panel/two-factor-setup" element={<Landed name="Set up two-factor" />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

async function fillPasswordStep(email = 'saad@saadsshop.pk', password = 'ChangeMe!Saad2026') {
  await userEvent.type(screen.getByLabelText('Email'), email);
  await userEvent.type(screen.getByLabelText('Password'), password);
  await userEvent.click(screen.getByRole('button', { name: 'Continue' }));
}

const enrolled  = { status: 200, body: { mfaToken: 'mfa-1', isTwoFactorEnrolled: true } };
const unenroled = { status: 200, body: { mfaToken: 'mfa-1', isTwoFactorEnrolled: false } };

beforeEach(() => { signIn.mockReset(); landedOn = ''; landedState = null; });
afterEach(() => vi.unstubAllGlobals());

describe('the password step', () => {
  it('sends what was typed', async () => {
    const fetchMock = apiServing({ '/auth/login': enrolled });
    vi.stubGlobal('fetch', fetchMock);

    renderSignIn();
    await fillPasswordStep();

    await waitFor(() => expect(bodyOf(fetchMock, '/auth/login')).toEqual({
      email: 'saad@saadsshop.pk', password: 'ChangeMe!Saad2026',
    }));
  });

  it('sends cookies, because the refresh cookie is set on this call', async () => {
    const fetchMock = apiServing({ '/auth/login': enrolled });
    vi.stubGlobal('fetch', fetchMock);

    renderSignIn();
    await fillPasswordStep();

    await waitFor(() => expect(initOf(fetchMock, '/auth/login')!.credentials).toBe('include'));
  });

  it('never hands over a session on the password alone', async () => {
    vi.stubGlobal('fetch', apiServing({ '/auth/login': enrolled }));

    renderSignIn();
    await fillPasswordStep();

    await screen.findByRole('heading', { name: 'One more step' });
    // The password buys a challenge token, not a session.
    expect(signIn).not.toHaveBeenCalled();
    expect(landedOn).toBe('');
  });

  it('moves to the code step for an enrolled account', async () => {
    vi.stubGlobal('fetch', apiServing({ '/auth/login': enrolled }));

    renderSignIn();
    await fillPasswordStep();

    expect(await screen.findByLabelText('Six-digit code')).toBeInTheDocument();
    expect(screen.queryByLabelText('Password')).not.toBeInTheDocument();
  });

  it('sends an account with no authenticator to set one up, not to a code it cannot produce', async () => {
    vi.stubGlobal('fetch', apiServing({ '/auth/login': unenroled }));

    renderSignIn();
    await fillPasswordStep();

    await waitFor(() => expect(landedOn).toBe('Set up two-factor'));
    // The challenge token travels with them; it is what opens enrolment.
    expect(landedState).toEqual({ mfaToken: 'mfa-1' });
  });

  it('says the one thing there is to say about a wrong password', async () => {
    vi.stubGlobal('fetch', apiServing({
      '/auth/login': { status: 401, body: { title: 'That email and password do not match.' } },
    }));

    renderSignIn();
    await fillPasswordStep();

    const alert = await screen.findByRole('alert');
    // The API answers the same for an unknown account and a wrong password, so
    // the screen must not add anything that distinguishes them.
    expect(alert).toHaveTextContent('That email and password do not match.');
    expect(alert.textContent).not.toMatch(/no such account|not found|unknown/i);
  });

  it('places a field error on its own field', async () => {
    vi.stubGlobal('fetch', apiServing({
      '/auth/login': {
        status: 400,
        body: { title: 'Check the fields.', errors: { Email: ['That is not an email address.'] } },
      },
    }));

    renderSignIn();
    await fillPasswordStep('not-an-email', 'x');

    await waitFor(() => expect(screen.getByLabelText('Email'))
      .toHaveAccessibleDescription('That is not an email address.'));
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('stays put when a rate limit stops the attempt', async () => {
    vi.stubGlobal('fetch', apiServing({
      '/auth/login': { status: 429, body: { title: 'Too many attempts. Try again in a few minutes.' } },
    }));

    renderSignIn();
    await fillPasswordStep();

    expect(await screen.findByRole('alert')).toHaveTextContent(/Too many attempts/);
    expect(screen.getByLabelText('Password')).toBeInTheDocument();
  });

  it('says it is working, and refuses a second submit while it is', async () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(() => {})));

    renderSignIn();
    await fillPasswordStep();

    const button = await screen.findByRole('button', { name: 'Checking…' });
    expect(button).toBeDisabled();
  });

  it('sends people to Google by a full-page redirect, not by fetch', () => {
    vi.stubGlobal('fetch', apiServing({}));

    renderSignIn();

    expect(screen.getByRole('link', { name: 'Continue with Google' }))
      .toHaveAttribute('href', '/api/auth/google');
  });
});

describe('the code step', () => {
  async function reachCodeStep(routes: Record<string, Reply | Reply[]> = {}) {
    const fetchMock = apiServing({ '/auth/login': enrolled, ...routes });
    vi.stubGlobal('fetch', fetchMock);

    renderSignIn();
    await fillPasswordStep();
    await screen.findByRole('heading', { name: 'One more step' });

    return fetchMock;
  }

  it('exchanges the challenge token and the code for a session', async () => {
    const fetchMock = await reachCodeStep({ '/auth/2fa/verify': { status: 200, body: aSession } });

    await userEvent.type(screen.getByLabelText('Six-digit code'), '123456');
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    await waitFor(() => expect(bodyOf(fetchMock, '/2fa/verify')).toEqual({
      mfaToken: 'mfa-1', code: '123456', isRecoveryCode: false,
    }));
  });

  it('hands the session to the app and opens the panel', async () => {
    await reachCodeStep({ '/auth/2fa/verify': { status: 200, body: aSession } });

    await userEvent.type(screen.getByLabelText('Six-digit code'), '123456');
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    await waitFor(() => expect(signIn).toHaveBeenCalledWith(expect.objectContaining({
      accessToken: 'access-1', email: 'saad@saadsshop.pk',
    })));
    await waitFor(() => expect(landedOn).toBe('Overview'));
  });

  it('lands where they were headed before being asked to sign in', async () => {
    const fetchMock = apiServing({
      '/auth/login': enrolled,
      '/auth/2fa/verify': { status: 200, body: aSession },
    });
    vi.stubGlobal('fetch', fetchMock);

    renderSignIn({ from: '/shop-panel/orders' });
    await fillPasswordStep();
    await screen.findByRole('heading', { name: 'One more step' });

    await userEvent.type(screen.getByLabelText('Six-digit code'), '123456');
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    await waitFor(() => expect(landedOn).toBe('Orders'));
  });

  it('reports a wrong code without throwing away the challenge', async () => {
    const fetchMock = await reachCodeStep({
      '/auth/2fa/verify': [
        { status: 401, body: { title: 'That code is not right.' } },
        { status: 200, body: aSession },
      ],
    });

    await userEvent.type(screen.getByLabelText('Six-digit code'), '000000');
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('That code is not right.');

    // A mistyped digit must not mean starting again from the password.
    await userEvent.clear(screen.getByLabelText('Six-digit code'));
    await userEvent.type(screen.getByLabelText('Six-digit code'), '123456');
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    await waitFor(() => expect(signIn).toHaveBeenCalled());
    expect(bodyOf(fetchMock, '/2fa/verify').mfaToken).toBe('mfa-1');
  });

  it('reports an expired challenge, which is the whole message there is', async () => {
    // The regression this names: every sign-in once failed here, because the
    // token was read back under a different claim name than it was written.
    await reachCodeStep({
      '/auth/2fa/verify': { status: 401, body: { title: 'That sign-in attempt has expired.' } },
    });

    await userEvent.type(screen.getByLabelText('Six-digit code'), '123456');
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('That sign-in attempt has expired.');
    expect(signIn).not.toHaveBeenCalled();
  });

  it('takes a recovery code instead, with the keyboard to match', async () => {
    const fetchMock = await reachCodeStep({ '/auth/2fa/verify': { status: 200, body: aSession } });

    await userEvent.click(screen.getByRole('button', { name: 'Use a recovery code' }));

    const input = screen.getByLabelText('Recovery code');
    expect(input).toHaveAttribute('inputMode', 'text');
    expect(input).toHaveAttribute('maxLength', '32');

    await userEvent.type(input, 'abcd-efgh');
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));

    await waitFor(() => expect(bodyOf(fetchMock, '/2fa/verify')).toEqual({
      mfaToken: 'mfa-1', code: 'abcd-efgh', isRecoveryCode: true,
    }));
  });

  it('asks for six digits on a numeric keypad', async () => {
    await reachCodeStep();

    const input = screen.getByLabelText('Six-digit code');
    expect(input).toHaveAttribute('inputMode', 'numeric');
    expect(input).toHaveAttribute('maxLength', '6');
    expect(input).toHaveAttribute('autoComplete', 'one-time-code');
  });

  it('clears a half-typed code when switching kinds, so the wrong one is not sent', async () => {
    await reachCodeStep();

    await userEvent.type(screen.getByLabelText('Six-digit code'), '1234');
    await userEvent.click(screen.getByRole('button', { name: 'Use a recovery code' }));

    expect(screen.getByLabelText('Recovery code')).toHaveValue('');
  });

  it('goes back to the password step, clearing the error with it', async () => {
    await reachCodeStep({
      '/auth/2fa/verify': { status: 401, body: { title: 'That code is not right.' } },
    });

    await userEvent.type(screen.getByLabelText('Six-digit code'), '000000');
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }));
    await screen.findByRole('alert');

    await userEvent.click(screen.getByRole('button', { name: 'Back' }));

    expect(screen.getByLabelText('Password')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('keeps the email typed, so going back is not starting over', async () => {
    await reachCodeStep();

    await userEvent.click(screen.getByRole('button', { name: 'Back' }));

    expect(screen.getByLabelText('Email')).toHaveValue('saad@saadsshop.pk');
  });
});
