import { beforeEach, describe, expect, it, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { api, setLoginRedirect } from '../services/api';
import { ACCESS_TOKEN_KEY } from '../services/session';
import { useAuthStore } from './auth-store';

/** A response-shaped object: these tests only use what the transport reads. */
function jsonResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    statusText: status === 401 ? 'Unauthorized' : 'OK',
    json: () => Promise.resolve(body),
  } as unknown as Response;
}

function emptyResponse(status = 204): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    statusText: 'No Content',
    json: () => Promise.reject(new Error('no body')),
  } as unknown as Response;
}

/** An unsigned token with the claims the API signs: `AuthTokenService` writes short JWT names. */
function makeToken(claims: Record<string, unknown>): string {
  const encode = (value: object) =>
    btoa(JSON.stringify(value)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  return `${encode({ alg: 'HS256', typ: 'JWT' })}.${encode(claims)}.signature`;
}

function superadminToken(exp = Math.floor(Date.now() / 1000) + 3600): string {
  return makeToken({ sub: 'u-1', email: 'root@imc.test', unique_name: 'root', role: ['superadmin'], exp });
}

describe('auth store', () => {
  const calls: { url: string; init: RequestInit | undefined }[] = [];
  const redirects: (string | undefined)[] = [];

  beforeEach(() => {
    setActivePinia(createPinia());
    localStorage.clear();
    calls.length = 0;
    redirects.length = 0;
    // A real redirect would navigate jsdom; this records it instead.
    setLoginRedirect((returnTo) => redirects.push(returnTo));
    vi.unstubAllGlobals();
    vi.stubGlobal(
      'fetch',
      vi.fn((url: string, init?: RequestInit) => {
        calls.push({ url, init });
        return Promise.resolve(jsonResponse({ detail: 'unexpected call' }, 500));
      }),
    );
  });

  it('signs in, keeps the token and reads the user out of it', async () => {
    const token = superadminToken();
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(jsonResponse({ access_token: token, expires_at_utc: null }))),
    );

    const auth = useAuthStore();
    expect(auth.isAuthenticated).toBe(false);

    const signedIn = await auth.login('root@imc.test', 'secret');

    expect(signedIn).toBe(true);
    expect(auth.token).toBe(token);
    expect(auth.email).toBe('root@imc.test');
    expect(auth.roles).toEqual(['superadmin']);
    expect(auth.isSuperadmin).toBe(true);
    expect(localStorage.getItem(ACCESS_TOKEN_KEY)).toBe(token);
    expect(auth.error).toBeNull();
  });

  it('reports bad credentials instead of pretending the session exists', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(emptyResponse(401))));

    const auth = useAuthStore();
    const signedIn = await auth.login('root@imc.test', 'wrong');

    expect(signedIn).toBe(false);
    expect(auth.isAuthenticated).toBe(false);
    expect(auth.error).toBe('Correo o contraseña incorrectos.');
    expect(localStorage.getItem(ACCESS_TOKEN_KEY)).toBeNull();
  });

  it('signs out: revokes on the API and forgets the token even if the API is unreachable', async () => {
    const token = superadminToken();
    localStorage.setItem(ACCESS_TOKEN_KEY, token);
    const auth = useAuthStore();
    expect(auth.isAuthenticated).toBe(true);

    vi.stubGlobal('fetch', vi.fn(() => Promise.reject(new Error('offline'))));

    await auth.signOut();

    expect(auth.token).toBeNull();
    expect(auth.user).toBeNull();
    expect(auth.isAuthenticated).toBe(false);
    expect(localStorage.getItem(ACCESS_TOKEN_KEY)).toBeNull();
  });

  it('trades the refresh cookie for a new token', async () => {
    const fresh = superadminToken();
    vi.stubGlobal(
      'fetch',
      vi.fn((url: string, init?: RequestInit) => {
        calls.push({ url, init });
        return Promise.resolve(jsonResponse({ access_token: fresh }));
      }),
    );

    const auth = useAuthStore();
    const refreshed = await auth.refresh();

    expect(refreshed).toBe(true);
    expect(auth.token).toBe(fresh);
    expect(localStorage.getItem(ACCESS_TOKEN_KEY)).toBe(fresh);
    // The cookie is HttpOnly and scoped to /api/auth: the browser may only send it with credentials.
    expect(calls[0]?.url).toContain('/auth/refresh');
    expect(calls[0]?.init?.credentials).toBe('include');
  });

  it('clears the session when the refresh is refused', async () => {
    localStorage.setItem(ACCESS_TOKEN_KEY, superadminToken());
    const auth = useAuthStore();
    expect(auth.isAuthenticated).toBe(true);

    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(emptyResponse(401))));

    const refreshed = await auth.refresh();

    expect(refreshed).toBe(false);
    expect(auth.isAuthenticated).toBe(false);
    expect(localStorage.getItem(ACCESS_TOKEN_KEY)).toBeNull();
  });

  it('retries a 401 once with a refreshed token and updates the store', async () => {
    const stale = makeToken({ email: 'root@imc.test', role: ['superadmin'], exp: 1 });
    const fresh = superadminToken();
    localStorage.setItem(ACCESS_TOKEN_KEY, stale);

    const auth = useAuthStore();
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(emptyResponse(401)) // the request with the stale token
      .mockResolvedValueOnce(jsonResponse({ access_token: fresh })) // POST /auth/refresh
      .mockResolvedValueOnce(jsonResponse({ tree: [], panels: [] })); // the retry
    vi.stubGlobal('fetch', fetchMock);

    const result = await api.get<{ tree: unknown[] }>('/item');

    expect(result.tree).toEqual([]);
    expect(fetchMock).toHaveBeenCalledTimes(3);
    // The retry carries the new token, not the stale one.
    const retryHeaders = (fetchMock.mock.calls[2]?.[1] as RequestInit | undefined)?.headers as
      | Record<string, string>
      | undefined;
    expect(retryHeaders?.Authorization).toBe(`Bearer ${fresh}`);
    expect(auth.token).toBe(fresh);
    expect(localStorage.getItem(ACCESS_TOKEN_KEY)).toBe(fresh);
  });

  it('does not retry twice: a second 401 runs the session out', async () => {
    localStorage.setItem(ACCESS_TOKEN_KEY, superadminToken());

    const auth = useAuthStore();
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(emptyResponse(401))
      .mockResolvedValueOnce(emptyResponse(401));
    vi.stubGlobal('fetch', fetchMock);

    await expect(api.get('/item')).rejects.toThrow(/sesión expiró/i);

    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(auth.isAuthenticated).toBe(false);
    expect(localStorage.getItem(ACCESS_TOKEN_KEY)).toBeNull();
    // The user is sent to the login page, and no attempt is made to guess where they were.
    expect(redirects).toHaveLength(1);
  });

  describe('roles and impersonation', () => {
    function signedInAs(roles: string[]) {
      localStorage.setItem(ACCESS_TOKEN_KEY, makeToken({ email: 'user@imc.test', role: roles }));
      return useAuthStore();
    }

    it('lets a lector read but not write', () => {
      const auth = signedInAs(['lector']);

      expect(auth.isAuthenticated).toBe(true);
      expect(auth.canWrite).toBe(false);
      expect(auth.canManageConfiguration).toBe(false);
      expect(auth.canManageUsers).toBe(false);
    });

    it('lets an operador write fiches but not open configuration or the dashboard', () => {
      const auth = signedInAs(['operador']);

      expect(auth.canWrite).toBe(true);
      expect(auth.canManageConfiguration).toBe(false);
      expect(auth.canManageUsers).toBe(false);
    });

    it('lets an admin into configuration but not into the dashboard', () => {
      const auth = signedInAs(['admin']);

      expect(auth.canWrite).toBe(true);
      expect(auth.canManageConfiguration).toBe(true);
      expect(auth.canManageUsers).toBe(false);
    });

    it('gives the superadmin the dashboard', () => {
      const auth = signedInAs(['superadmin']);

      expect(auth.isSuperadmin).toBe(true);
      expect(auth.canManageUsers).toBe(true);
    });

    it('shows the banner while impersonating and disables every write', () => {
      const auth = signedInAs(['superadmin']);
      expect(auth.isImpersonating).toBe(false);
      expect(auth.impersonationNotice).toBeNull();
      expect(auth.canWrite).toBe(true);

      auth.startImpersonation({
        id: 'u-2',
        email: 'lector@imc.test',
        name: 'lector@imc.test',
        roles: ['lector'],
      });

      expect(auth.isImpersonating).toBe(true);
      expect(auth.impersonationNotice).toBe('Estás suplantando a lector@imc.test');
      // Even the superadmin may not write while wearing somebody else's identity.
      expect(auth.canWrite).toBe(false);
      expect(auth.canManageConfiguration).toBe(false);
      expect(auth.canManageUsers).toBe(false);
      // The real roles are untouched: leaving the impersonation restores the session.
      expect(auth.isSuperadmin).toBe(true);

      auth.stopImpersonation();

      expect(auth.isImpersonating).toBe(false);
      expect(auth.impersonationNotice).toBeNull();
      expect(auth.canWrite).toBe(true);
    });

    it('ends the impersonation when the session is lost', async () => {
      const auth = signedInAs(['superadmin']);
      auth.startImpersonation({
        id: 'u-2',
        email: 'lector@imc.test',
        name: 'lector@imc.test',
        roles: ['lector'],
      });

      vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(emptyResponse(401))));
      await api.get('/item').catch(() => undefined);

      expect(auth.isImpersonating).toBe(false);
      expect(auth.isAuthenticated).toBe(false);
    });
  });
});
