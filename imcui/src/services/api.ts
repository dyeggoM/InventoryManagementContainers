import { clearStoredToken, getStoredToken, storeToken } from './session';

// Prefer the configured URL (see .env.example). Without one, assume the API runs beside the page on
// port 5173: deriving the host from location keeps the app usable when it is opened from another
// machine on the LAN, where "localhost" would point at the browser's own PC.
const configuredBaseUrl = (import.meta.env.QCLI_API_URL ?? '').trim();
export const API_BASE_URL = configuredBaseUrl || `http://${window.location.hostname}:5173/api`;
// const API_BASE_URL = 'https://your-api.com/api';

export class ApiError extends Error {
  constructor(
    public status: number,
    public statusText: string,
    public data?: unknown,
    message?: string,
  ) {
    super(message ?? `API Error ${status}: ${statusText}`);
    this.name = 'ApiError';
  }
}

/**
 * The API answers 404 for a route it has not built yet. The dashboard uses this to show an honest
 * "API pendiente" state instead of an empty table that looks like a bug.
 */
export function isApiPending(error: unknown): boolean {
  return error instanceof ApiError && error.status === 404;
}

/** Pulls the human-readable part out of an ASP.NET problem-details style payload. */
function serverMessage(data: unknown): string | undefined {
  if (!data || typeof data !== 'object') return undefined;
  const record = data as Record<string, unknown>;
  for (const key of ['detail', 'message', 'title', 'error']) {
    const value = record[key];
    if (typeof value === 'string' && value.trim()) return value;
    if (Array.isArray(value) && value.length) return value.map(String).join('; ');
  }
  return undefined;
}

export function getErrorMessage(error: unknown): string {
  if (error instanceof ApiError) return error.message;
  if (error instanceof Error) return error.message;
  return String(error);
}

/** Login and refresh answer 401 for a bad credential: that must not bounce the page to /login. */
function isAuthEndpoint(endpoint: string): boolean {
  return endpoint.startsWith('/auth/');
}

export type SessionEvent = { type: 'refreshed'; token: string } | { type: 'expired' };

type SessionListener = (event: SessionEvent) => void;
const sessionListeners = new Set<SessionListener>();

/**
 * The transport refreshes and expires sessions on its own; the store subscribes so what it shows
 * never disagrees with what the requests actually did.
 */
export function onSessionEvent(listener: SessionListener): () => void {
  sessionListeners.add(listener);
  return () => {
    sessionListeners.delete(listener);
  };
}

function emitSessionEvent(event: SessionEvent): void {
  for (const listener of [...sessionListeners]) listener(event);
}

let loginRedirect: (returnTo?: string) => void = defaultLoginRedirect;

/** The router replaces this with `router.replace('/login')`; a test points it at a spy. */
export function setLoginRedirect(handler: (returnTo?: string) => void): void {
  loginRedirect = handler;
}

/** The route the browser is on, so the login page can send the user back after signing in. */
function currentPath(): string {
  const hash = typeof window === 'undefined' ? '' : window.location.hash.replace(/^#/, '');
  return hash && !hash.startsWith('/login') ? hash : '';
}

function defaultLoginRedirect(returnTo?: string): void {
  if (typeof window === 'undefined') return;
  if (window.location.hash.startsWith('#/login')) return;
  const query = returnTo ? `?redirect=${encodeURIComponent(returnTo)}` : '';
  try {
    // Hash mode (see quasar.config.ts): the path before the hash stays untouched.
    window.location.assign(`${window.location.pathname}${window.location.search}#/login${query}`);
  } catch {
    // jsdom refuses to navigate; a test that reaches here asserts the decision, not the page.
  }
}

function expireSession(): void {
  clearStoredToken();
  emitSessionEvent({ type: 'expired' });
  loginRedirect(currentPath());
}

let refreshInFlight: Promise<string | null> | null = null;

/**
 * Trades the HttpOnly `imc_refresh` cookie for a new access token. Concurrent 401s share one
 * request: the API rotates the refresh token on every call, so a second in-flight refresh would
 * present a token the first one already revoked.
 */
export function refreshAccessToken(): Promise<string | null> {
  if (!refreshInFlight) {
    refreshInFlight = performRefresh().finally(() => {
      refreshInFlight = null;
    });
  }
  return refreshInFlight;
}

async function performRefresh(): Promise<string | null> {
  try {
    const response = await fetch(`${API_BASE_URL}/auth/refresh`, {
      method: 'POST',
      // The cookie is HttpOnly and scoped to /api/auth: only the browser can present it.
      credentials: 'include',
      headers: { 'Content-Type': 'application/json' },
    });
    if (!response.ok) return null;

    const data = (await response.json()) as { access_token?: string } | null;
    const token = data?.access_token;
    if (!token) return null;

    storeToken(token);
    emitSessionEvent({ type: 'refreshed', token });
    return token;
  } catch {
    // A network failure here means the same as a refused refresh: the caller decides what to show.
    return null;
  }
}

async function send(endpoint: string, options: RequestInit, isFormData: boolean): Promise<Response> {
  // Read the token per request, never at import time: it changes on login, refresh and logout.
  const token = getStoredToken();
  try {
    return await fetch(`${API_BASE_URL}${endpoint}`, {
      ...options,
      headers: {
        ...(isFormData ? {} : { 'Content-Type': 'application/json' }),
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...options.headers,
      },
    });
  } catch {
    // fetch only rejects on network/CORS failures, which are otherwise invisible in the UI.
    throw new ApiError(0, 'Network error', undefined, `Cannot reach the API at ${API_BASE_URL}`);
  }
}

/**
 * Sends the request, recovers the session once on a 401 and turns a failed status into an
 * `ApiError`. Both the JSON and the byte path go through here, so a download is signed, refreshed
 * and reported exactly like the calls around it.
 */
async function sendWithSession(
  endpoint: string,
  options: RequestInit,
  isFormData: boolean,
): Promise<Response> {
  let response = await send(endpoint, options, isFormData);

  if (response.status === 401 && !isAuthEndpoint(endpoint)) {
    // An expired access token is normal: the refresh cookie outlives it by days. Retry once with
    // the new token; a second 401 means the session is gone, so clear it and go to the login page.
    const refreshed = await refreshAccessToken();
    if (refreshed) response = await send(endpoint, options, isFormData);
    if (!refreshed || response.status === 401) {
      expireSession();
      throw new ApiError(
        response.status,
        response.statusText,
        undefined,
        'Tu sesión expiró. Inicia sesión de nuevo.',
      );
    }
  }

  if (!response.ok) {
    let data: unknown;
    try {
      data = await response.json();
    } catch {
      data = undefined;
    }

    throw new ApiError(response.status, response.statusText, data, serverMessage(data));
  }

  return response;
}

async function request<T>(endpoint: string, options: RequestInit = {}): Promise<T> {
  // FormData must go out untouched: the browser writes the multipart boundary itself, and a
  // JSON content type would make the server reject the body.
  const isFormData = typeof FormData !== 'undefined' && options.body instanceof FormData;
  const response = await sendWithSession(endpoint, options, isFormData);

  if (response.status === 204) {
    return undefined as T;
  }

  return response.json() as Promise<T>;
}

/**
 * The same request as `get`, answered as bytes instead of JSON. A file the API serves behind the
 * token cannot be handed to the browser as a link: a new tab carries no `Authorization` header, so
 * the route answers 401 and the tab stays blank. The blob is what an in-app preview and a download
 * can actually use.
 */
async function requestBlob(endpoint: string, options: RequestInit): Promise<Blob> {
  const response = await sendWithSession(endpoint, options, false);
  return response.blob();
}

export const api = {
  get<T>(endpoint: string, options?: RequestInit): Promise<T> {
    return request<T>(endpoint, {
      ...options,
      method: 'GET',
    });
  },

  /** A file behind the bearer token, fetched with the session the app already holds. */
  getBlob(endpoint: string, options?: RequestInit): Promise<Blob> {
    return requestBlob(endpoint, {
      ...options,
      method: 'GET',
    });
  },

  /** Multipart upload: the browser sets the boundary, so no Content-Type header here. */
  postForm<T>(endpoint: string, form: FormData): Promise<T> {
    return request<T>(endpoint, { method: 'POST', body: form, headers: {} });
  },

  post<T>(endpoint: string, body?: unknown, options?: RequestInit): Promise<T> {
    const requestOptions: RequestInit = {
      ...options,
      method: 'POST',
    };

    if (body !== undefined) {
      requestOptions.body = JSON.stringify(body);
    }

    return request<T>(endpoint, requestOptions);
  },

  put<T>(endpoint: string, body?: unknown, options?: RequestInit): Promise<T> {
    const requestOptions: RequestInit = {
      ...options,
      method: 'PUT',
    };

    if (body !== undefined) {
      requestOptions.body = JSON.stringify(body);
    }

    return request<T>(endpoint, requestOptions);
  },

  /** Partial update: the archive's activate route is a PATCH with no body. */
  patch<T>(endpoint: string, body?: unknown, options?: RequestInit): Promise<T> {
    const requestOptions: RequestInit = {
      ...options,
      method: 'PATCH',
    };

    if (body !== undefined) {
      requestOptions.body = JSON.stringify(body);
    }

    return request<T>(endpoint, requestOptions);
  },

  delete<T>(endpoint: string, options?: RequestInit): Promise<T> {
    return request<T>(endpoint, {
      ...options,
      method: 'DELETE',
    });
  },
};
