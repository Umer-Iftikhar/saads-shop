import type { ProblemDetails } from '../types/api';

/**
 * The API client.
 *
 * Thin on purpose: one `request` function that adds the base URL, sends and
 * parses JSON, and turns any failure into an {@link ApiError} carrying the
 * server's problem details. Everything else is a typed wrapper over it.
 */

const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '/api';

/**
 * A failed request, carrying what the server actually said.
 *
 * `fieldErrors` lets a form put a message next to the input that caused it,
 * rather than showing one banner for a validation failure the user then has to
 * go hunting for.
 */
export class ApiError extends Error {
  readonly status: number;
  readonly problem: ProblemDetails | null;
  readonly correlationId?: string;

  constructor(status: number, problem: ProblemDetails | null, fallback: string) {
    super(problem?.title || fallback);
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
    this.correlationId = problem?.correlationId;
  }

  get fieldErrors(): Record<string, string[]> {
    return this.problem?.errors ?? {};
  }

  /** True when the failure is one the customer can fix by changing something. */
  get isValidation(): boolean {
    return this.status === 400;
  }

  /** A business rule refused — out of stock, an illegal move, a duplicate. */
  get isConflict(): boolean {
    return this.status === 409;
  }

  get isUnauthorised(): boolean {
    return this.status === 401;
  }
}

interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE';
  body?: unknown;
  signal?: AbortSignal;
  /** Sends the refresh cookie. Only the auth endpoints need it. */
  withCredentials?: boolean;
  /**
   * Overrides the stored access token for this one call.
   *
   * Enrolling an authenticator during a first sign-in is the case that needs
   * it: at that moment there is no session, only the short-lived challenge
   * token the login step returned. It carries no roles, so it opens the
   * enrolment endpoints and nothing else.
   */
  bearer?: string;
}

/** Set after sign-in. Held in memory only — never localStorage, where XSS could read it. */
let accessToken: string | null = null;

export function setAccessToken(token: string | null): void {
  accessToken = token;
}

export function getAccessToken(): string | null {
  return accessToken;
}

async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = 'GET', body, signal, withCredentials = false, bearer } = options;

  //  FormData goes through as it is. Setting Content-Type on a multipart body
  //  would omit the boundary the browser generates, and the server would have
  //  no way to find where each part starts.
  const isMultipart = body instanceof FormData;

  const headers: Record<string, string> = { Accept: 'application/json' };
  if (body !== undefined && !isMultipart) headers['Content-Type'] = 'application/json';

  const token = bearer ?? accessToken;
  if (token) headers.Authorization = `Bearer ${token}`;

  let response: Response;

  try {
    response = await fetch(`${BASE_URL}${path}`, {
      method,
      headers,
      body: body === undefined ? undefined : isMultipart ? (body as FormData) : JSON.stringify(body),
      signal,
      credentials: withCredentials ? 'include' : 'same-origin',
    });
  } catch (cause) {
    // A network failure, not an HTTP one — there is no status and no problem
    // document. Say something a shopper can act on rather than surfacing
    // "TypeError: Failed to fetch".
    if (signal?.aborted) throw cause;
    throw new ApiError(0, null, 'Could not reach the shop. Check your connection and try again.');
  }

  if (response.status === 204) return undefined as T;

  const isJson = response.headers.get('content-type')?.includes('json') ?? false;
  const payload = isJson ? await response.json().catch(() => null) : null;

  if (!response.ok) {
    throw new ApiError(response.status, payload as ProblemDetails | null,
      'Something went wrong. Please try again.');
  }

  return payload as T;
}

export const api = {
  get:   <T>(path: string, signal?: AbortSignal) => request<T>(path, { signal }),
  post:  <T>(path: string, body?: unknown, opts?: RequestOptions) =>
           request<T>(path, { ...opts, method: 'POST', body }),
  put:   <T>(path: string, body?: unknown) => request<T>(path, { method: 'PUT', body }),
  patch: <T>(path: string, body?: unknown) => request<T>(path, { method: 'PATCH', body }),
  del:   <T>(path: string) => request<T>(path, { method: 'DELETE' }),
};

/**
 * Builds a query string, dropping anything empty.
 *
 * Without this an untouched filter sends `?search=&status=` and the API has to
 * treat blank as absent — a rule that has to be remembered in two places.
 */
export function queryString(params: Record<string, string | number | boolean | null | undefined>): string {
  const search = new URLSearchParams();

  for (const [key, value] of Object.entries(params)) {
    if (value === null || value === undefined || value === '') continue;
    search.set(key, String(value));
  }

  const text = search.toString();
  return text ? `?${text}` : '';
}
