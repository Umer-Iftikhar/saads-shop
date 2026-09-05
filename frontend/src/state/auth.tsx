import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { api, ApiError, setAccessToken } from '../lib/api';

/**
 * Staff authentication.
 *
 * The access token is held **in memory only**. Putting it in localStorage would
 * mean any XSS bug could read it, and there is no need: the refresh token lives
 * in an HttpOnly cookie the browser sends automatically, so a page reload
 * recovers the session by calling /auth/refresh rather than by having kept a
 * copy of the token lying around.
 *
 * That is why `bootstrapping` exists — on first mount the app does not yet know
 * whether there is a session, and rendering the sign-in screen before finding
 * out would flash the login page at an already-signed-in shopkeeper.
 */

export interface CurrentUser {
  userId: string;
  email: string;
  fullName: string;
  roles: string[];
}

interface AuthContextValue {
  user: CurrentUser | null;
  /** True until the first refresh attempt settles. */
  bootstrapping: boolean;
  isOwner: boolean;
  signIn: (auth: AuthPayload) => void;
  signOut: () => Promise<void>;
}

interface AuthPayload {
  accessToken: string;
  expiresAt: string;
  userId: string;
  email: string;
  fullName: string;
  roles: string[];
}

const AuthContext = createContext<AuthContextValue | null>(null);

/**
 * Refresh this long before the token actually expires, so a request is never
 * sent with a token that expires in flight.
 */
const REFRESH_MARGIN_MS = 60_000;

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<CurrentUser | null>(null);
  const [bootstrapping, setBootstrapping] = useState(true);
  const refreshTimer = useRef<number | undefined>(undefined);

  /**
   * The refresh currently in flight, if any.
   *
   * Refresh tokens rotate, and presenting a spent one is treated as theft: the
   * server revokes the whole family. Two refreshes racing therefore do not
   * merely duplicate work, they end the session — the second call arrives with
   * the token the first has already burned. It happens more easily than it
   * sounds: React's StrictMode runs the mount effect twice in development, and
   * in production several requests failing at once would each want to refresh.
   *
   * So there is only ever one; everybody else waits on it.
   */
  const inFlight = useRef<Promise<boolean> | null>(null);

  const applySession = useCallback((auth: AuthPayload) => {
    setAccessToken(auth.accessToken);
    setUser({
      userId: auth.userId,
      email: auth.email,
      fullName: auth.fullName,
      roles: auth.roles,
    });

    // Schedule the next rotation. Each refresh returns a new token and a new
    // expiry, so this re-arms itself for as long as the session lives.
    window.clearTimeout(refreshTimer.current);
    const msUntilRefresh = new Date(auth.expiresAt).getTime() - Date.now() - REFRESH_MARGIN_MS;
    refreshTimer.current = window.setTimeout(() => { void refresh(); }, Math.max(msUntilRefresh, 5_000));
  }, []);

  const clearSession = useCallback(() => {
    window.clearTimeout(refreshTimer.current);
    setAccessToken(null);
    setUser(null);
  }, []);

  const refresh = useCallback(() => {
    if (inFlight.current) return inFlight.current;

    const attempt = (async () => {
      try {
        // withCredentials so the HttpOnly refresh cookie rides along; it is the
        // only thing that proves who this is.
        const auth = await api.post<AuthPayload>('/auth/refresh', undefined, { withCredentials: true });
        applySession(auth);
        return true;
      } catch (error) {
        // 401 here is ordinary: no session, or the refresh token was rotated
        // away — including by the reuse detection that revokes a whole family.
        if (!(error instanceof ApiError) || error.status !== 401) {
          console.warn('Session refresh failed', error);
        }
        clearSession();
        return false;
      } finally {
        inFlight.current = null;
      }
    })();

    inFlight.current = attempt;
    return attempt;
  }, [applySession, clearSession]);

  // One attempt on mount to recover an existing session.
  useEffect(() => {
    let cancelled = false;

    void refresh().finally(() => {
      if (!cancelled) setBootstrapping(false);
    });

    return () => {
      cancelled = true;
      window.clearTimeout(refreshTimer.current);
    };
  }, [refresh]);

  const signOut = useCallback(async () => {
    try {
      await api.post('/auth/logout', undefined, { withCredentials: true });
    } catch {
      // Even if the call fails the local session must go — leaving the UI
      // signed in after someone pressed "sign out" is the worse outcome.
    }
    clearSession();
  }, [clearSession]);

  const value = useMemo<AuthContextValue>(() => ({
    user,
    bootstrapping,
    isOwner: user?.roles.includes('Owner') ?? false,
    signIn: applySession,
    signOut,
  }), [user, bootstrapping, applySession, signOut]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) throw new Error('useAuth must be used inside an AuthProvider');
  return context;
}
