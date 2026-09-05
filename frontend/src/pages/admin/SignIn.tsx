import { useState } from 'react';
import { useMutation } from '@tanstack/react-query';
import { useNavigate, useLocation } from 'react-router-dom';
import { Logo } from '../../components/Logo';
import { Field } from '../../components/Field';
import { api, ApiError } from '../../lib/api';
import { fabricBackground } from '../../lib/fabric';
import { fromApiErrors } from '../../lib/validation';
import type { FieldErrors } from '../../lib/validation';
import { useAuth } from '../../state/auth';
import type { AuthResult, LoginChallenge } from '../../types/admin';

/**
 * Staff sign-in. Not in the prototype — the design has no auth screens at all —
 * so it is built from the same tokens: cream ground, terracotta accent,
 * Caprasimo headings, pill controls, and a panel of woven cloth alongside.
 *
 * Two steps, because the password alone never yields a session: the API returns
 * a short-lived challenge token that opens the 2FA endpoint and nothing else.
 */
export function SignIn() {
  const navigate = useNavigate();
  const location = useLocation();
  const { signIn } = useAuth();

  const [step, setStep]         = useState<'password' | 'code'>('password');
  const [email, setEmail]       = useState('');
  const [password, setPassword] = useState('');
  const [code, setCode]         = useState('');
  const [useRecovery, setUseRecovery] = useState(false);
  const [mfaToken, setMfaToken] = useState('');
  const [errors, setErrors]     = useState<FieldErrors>({});
  const [formError, setFormError] = useState<string | null>(null);

  // Where the shopkeeper was headed before being asked to sign in.
  const intended = (location.state as { from?: string } | null)?.from ?? '/shop-panel';

  const login = useMutation({
    mutationFn: () => api.post<LoginChallenge>('/auth/login', { email, password }, { withCredentials: true }),
    onSuccess: challenge => {
      setMfaToken(challenge.mfaToken);
      setFormError(null);
      setErrors({});

      if (!challenge.isTwoFactorEnrolled) {
        // The account has a password but no authenticator yet. Sending them to
        // a code prompt they cannot answer would be a dead end.
        navigate('/shop-panel/two-factor-setup', { state: { mfaToken: challenge.mfaToken }, replace: true });
        return;
      }

      setStep('code');
    },
    onError: handleError,
  });

  const verify = useMutation({
    mutationFn: () => api.post<AuthResult>(
      '/auth/2fa/verify',
      { mfaToken, code, isRecoveryCode: useRecovery },
      { withCredentials: true },
    ),
    onSuccess: auth => {
      signIn(auth);
      navigate(intended, { replace: true });
    },
    onError: handleError,
  });

  function handleError(error: ApiError) {
    if (error.isValidation && Object.keys(error.fieldErrors).length) {
      setErrors(fromApiErrors(error.fieldErrors));
      setFormError(null);
    } else {
      // The API deliberately answers the same way for an unknown account and a
      // wrong password, so this message is all there is — and all there should be.
      setFormError(error.message);
    }
  }

  const busy = login.isPending || verify.isPending;

  return (
    <main id="main" className="grid min-h-screen grid-cols-1 lg:grid-cols-[0.9fr_1.1fr]">
      {/*  Cloth panel — the shop's own palette, standing in for photography.
          It is decoration, so below the split it goes rather than costing a
          screen of scrolling before the form.                              */}
      <div
        className="washed hidden lg:block"
        aria-hidden="true"
        style={{ background: fabricBackground('#c67139', 'Woven') }}
      />

      <div className="flex items-start justify-center bg-bg px-5 py-8 lg:items-center lg:px-10 lg:py-12">
        <div className="w-full max-w-[380px]">
          <div className="mb-8 flex items-center gap-2.5">
            <Logo size={44} />
            <div>
              <div className="font-heading text-xl/none">
                Saad&rsquo;s Shop
              </div>
              <div className="mt-[3px] text-[10px] uppercase tracking-[0.16em] text-neutral-600">
                Shop panel
              </div>
            </div>
          </div>

          {step === 'password' ? (
            <form
              onSubmit={e => { e.preventDefault(); setFormError(null); login.mutate(); }}
              noValidate
            >
              <h1 className="mb-1.5 mt-0 text-[38px]">Sign in</h1>
              <p className="mb-6 mt-0 text-[15px] text-neutral-700">
                For Saad and the shop staff.
              </p>

              <Field label="Email" error={errors.email}>
                {props => (
                  <input
                    {...props}
                    className="input"
                    type="email"
                    autoComplete="username"
                    autoFocus
                    value={email}
                    onChange={e => setEmail(e.target.value)}
                  />
                )}
              </Field>

              <Field label="Password" error={errors.password}>
                {props => (
                  <input
                    {...props}
                    className="input"
                    type="password"
                    autoComplete="current-password"
                    value={password}
                    onChange={e => setPassword(e.target.value)}
                  />
                )}
              </Field>

              {formError && <FormError>{formError}</FormError>}

              <button type="submit" className="btn btn-primary btn-block btn-lg mt-4" disabled={busy}>
                {login.isPending ? 'Checking…' : 'Continue'}
              </button>

              <div className="my-5 flex items-center gap-3">
                <span className="h-px flex-1 bg-divider" />
                <span className="text-xs text-neutral-600">or</span>
                <span className="h-px flex-1 bg-divider" />
              </div>

              {/*  A plain link, not fetch: the OAuth flow is a full-page
                  redirect to Google and back.                                */}
              <a className="btn btn-secondary btn-block btn-lg" href="/api/auth/google">
                Continue with Google
              </a>

              <p className="mt-5 text-center text-xs text-neutral-600">
                Every shop account uses a second step. Ask Saad if you need one.
              </p>
            </form>
          ) : (
            <form
              onSubmit={e => { e.preventDefault(); setFormError(null); verify.mutate(); }}
              noValidate
            >
              <h1 className="mb-1.5 mt-0 text-[38px]">One more step</h1>
              <p className="mb-6 mt-0 text-[15px] text-neutral-700">
                {useRecovery
                  ? 'Enter one of the recovery codes you saved.'
                  : 'Enter the six-digit code from your authenticator app.'}
              </p>

              <Field label={useRecovery ? 'Recovery code' : 'Six-digit code'} error={errors.code}>
                {props => (
                  <input
                    {...props}
                    // A numeric keypad on a phone for the TOTP; recovery codes
                    // contain letters, so the keyboard has to change with it.
                    inputMode={useRecovery ? 'text' : 'numeric'}
                    autoComplete="one-time-code"
                    autoFocus
                    maxLength={useRecovery ? 32 : 6}
                    placeholder={useRecovery ? 'abcd-efgh' : '000000'}
                    className={`input text-center text-lg ${useRecovery ? '' : 'tracking-[0.4em]'}`}
                    value={code}
                    onChange={e => setCode(e.target.value)}
                  />
                )}
              </Field>

              {formError && <FormError>{formError}</FormError>}

              <button type="submit" className="btn btn-primary btn-block btn-lg mt-4" disabled={busy}>
                {verify.isPending ? 'Checking…' : 'Sign in'}
              </button>

              <div className="mt-4 flex justify-between gap-2.5">
                <button
                  type="button"
                  className="btn btn-ghost"
                  onClick={() => { setUseRecovery(!useRecovery); setCode(''); setFormError(null); }}
                >
                  {useRecovery ? 'Use the app instead' : 'Use a recovery code'}
                </button>

                <button
                  type="button"
                  className="btn btn-ghost"
                  onClick={() => { setStep('password'); setCode(''); setFormError(null); }}
                >
                  Back
                </button>
              </div>
            </form>
          )}
        </div>
      </div>
    </main>
  );
}

function FormError({ children }: { children: React.ReactNode }) {
  return (
    <div role="alert" className="mt-3.5 rounded-md bg-accent-100 px-4 py-3 text-sm text-accent-800">
      {children}
    </div>
  );
}
