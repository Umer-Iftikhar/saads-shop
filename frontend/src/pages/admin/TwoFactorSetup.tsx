import { useState } from 'react';
import { useMutation } from '@tanstack/react-query';
import { useLocation, useNavigate } from 'react-router-dom';
import { Logo } from '../../components/Logo';
import { Field } from '../../components/Field';
import { api, ApiError } from '../../lib/api';
import { fabricBackground } from '../../lib/fabric';
import { useAuth } from '../../state/auth';
import type { RecoveryCodes, TwoFactorSetup as Setup } from '../../types/admin';

/**
 * Enrolling an authenticator.
 *
 * The secret is shown once and never retrievable afterwards — an endpoint that
 * could re-read it would turn any hijacked session into a permanent bypass of
 * the second factor. Same for the recovery codes.
 *
 * No QR image: generating one would mean pulling in a library, and the
 * otpauth:// URI can be pasted into any authenticator by hand. The manual key
 * is shown in readable groups for the same reason.
 */
export function TwoFactorSetup() {
  const navigate = useNavigate();
  const location = useLocation();
  const { user } = useAuth();

  const [setup, setSetup]   = useState<Setup | null>(null);
  const [code, setCode]     = useState('');
  const [codes, setCodes]   = useState<string[] | null>(null);
  const [error, setError]   = useState<string | null>(null);
  const [saved, setSaved]   = useState(false);

  /*  Two ways in. A first sign-in arrives with the challenge token the login
      step returned and no session at all; someone already signed in arrives
      from Settings, and the stored access token covers them. The challenge
      token carries no roles, so it opens these two endpoints and nothing else. */
  const mfaToken = (location.state as { mfaToken?: string } | null)?.mfaToken;
  const enrolling = Boolean(mfaToken) && !user;

  const begin = useMutation({
    mutationFn: () => api.post<Setup>('/auth/2fa/enroll', undefined, { bearer: mfaToken }),
    onSuccess: setSetup,
    onError: (e: ApiError) => setError(e.message),
  });

  const confirm = useMutation({
    mutationFn: () => api.post<RecoveryCodes>('/auth/2fa/confirm', { code }, { bearer: mfaToken }),
    onSuccess: result => { setCodes(result.recoveryCodes); setError(null); },
    onError: (e: ApiError) => setError(e.message),
  });

  return (
    <main id="main" className="grid min-h-screen grid-cols-1 lg:grid-cols-[0.9fr_1.1fr]">
      <div
        className="washed hidden lg:block"
        aria-hidden="true"
        style={{ background: fabricBackground('#7a8a5e', 'Woven') }}
      />

      <div className="flex items-start justify-center bg-bg px-5 py-8 lg:items-center lg:px-10 lg:py-12">
        <div className="w-full max-w-[420px]">
          <div className="mb-7 flex items-center gap-2.5">
            <Logo size={40} />
            <div className="font-heading text-lg">Saad&rsquo;s Shop</div>
          </div>

          {codes ? (
            <>
              <h1 className="mb-1.5 mt-0 text-[34px]">Save these codes</h1>
              <p className="mb-5 mt-0 text-[15px] text-neutral-700">
                Each one signs you in once if you lose your phone. They are shown now and never again —
                write them down and keep them somewhere safe, not on the phone itself.
              </p>

              <ul className="m-0 grid list-none grid-cols-1 gap-x-5 gap-y-2.5 rounded-md
                             border border-divider bg-surface px-5 py-4 font-mono text-[15px] xs:grid-cols-2">
                {codes.map(c => <li key={c}>{c}</li>)}
              </ul>

              <label className="my-5 flex items-start gap-2.5 text-sm">
                <input
                  type="checkbox"
                  checked={saved}
                  onChange={e => setSaved(e.target.checked)}
                  className="mt-0.5 h-[18px] w-[18px] accent-accent"
                />
                I have written these down somewhere safe.
              </label>

              <button
                type="button"
                className="btn btn-primary btn-block btn-lg"
                disabled={!saved}
                /*  Enrolling during a first sign-in leaves no session — the
                    challenge token opened these two endpoints and nothing
                    else — so that path goes back to sign in properly. Someone
                    who came from Settings already has one.                  */
                onClick={() => navigate(enrolling ? '/shop-panel/sign-in' : '/shop-panel', { replace: true })}
              >
                {enrolling ? 'Sign in with your new code' : 'Go to the shop panel'}
              </button>
            </>
          ) : setup ? (
            <form onSubmit={e => { e.preventDefault(); setError(null); confirm.mutate(); }} noValidate>
              <h1 className="mb-1.5 mt-0 text-[34px]">Set up your app</h1>
              <p className="mb-4 mt-0 text-[15px] text-neutral-700">
                Add this key to Google Authenticator, Authy or any authenticator app, then type the
                six digits it shows.
              </p>

              <div className="mb-1.5 text-xs text-neutral-600">SETUP KEY</div>
              <code className="block break-all rounded-md border border-divider bg-surface px-4 py-3.5
                               font-mono text-base tracking-[0.08em]">
                {setup.sharedKey.match(/.{1,4}/g)?.join(' ')}
              </code>

              <details className="mb-5 mt-3">
                <summary className="cursor-pointer text-[13px] text-accent-700">
                  Or paste this link into your app
                </summary>
                <code className="mt-2 block break-all text-[11px] text-neutral-700">
                  {setup.authenticatorUri}
                </code>
              </details>

              <Field label="Six-digit code">
                {props => (
                  <input
                    {...props}
                    inputMode="numeric"
                    autoComplete="one-time-code"
                    maxLength={6}
                    placeholder="000000"
                    className="input text-center text-lg tracking-[0.4em]"
                    value={code}
                    onChange={e => setCode(e.target.value)}
                  />
                )}
              </Field>

              {error && (
                <div role="alert" className="mt-3.5 rounded-md bg-accent-100 px-4 py-3 text-sm text-accent-800">{error}</div>
              )}

              <button type="submit" className="btn btn-primary btn-block btn-lg mt-4"
                      disabled={confirm.isPending}>
                {confirm.isPending ? 'Checking…' : 'Turn on two-factor'}
              </button>
            </form>
          ) : (
            <>
              <h1 className="mb-1.5 mt-0 text-[34px]">Add a second step</h1>
              <p className="mb-5 mt-0 text-[15px] text-neutral-700">
                {user ? `Signed in as ${user.email}. ` : ''}
                The shop panel holds every order and the shop&rsquo;s settings, so a password on its
                own is not enough to open it.
              </p>

              {error && (
                <div role="alert" className="mb-3.5 rounded-md bg-accent-100 px-4 py-3 text-sm text-accent-800">{error}</div>
              )}

              <button type="button" className="btn btn-primary btn-block btn-lg"
                      onClick={() => { setError(null); begin.mutate(); }} disabled={begin.isPending}>
                {begin.isPending ? 'Preparing…' : 'Start setup'}
              </button>
            </>
          )}
        </div>
      </div>
    </main>
  );
}
