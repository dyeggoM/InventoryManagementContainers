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

/** The JSON body the transport sent, read without stringifying a `BodyInit` blindly. */
function postedBody(calls: { init: RequestInit | undefined }[]): unknown {
  const body = calls[0]?.init?.body;
  return typeof body === 'string' ? JSON.parse(body) : undefined;
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

    /** The token the API signs for a borrowed role: one role plus the caller in `act`. */
    function borrowedToken(role: string): string {
      return makeToken({
        sub: 'u-1',
        email: 'root@imc.test',
        unique_name: 'root',
        role: [role],
        act: 'u-1',
        act_email: 'root@imc.test',
        scope: 'read',
        exp: Math.floor(Date.now() / 1000) + 420,
      });
    }

    function impersonationAnswer(role: string) {
      return {
        access_token: borrowedToken(role),
        expires_at_utc: null,
        role,
        scope: 'read',
        impersonated_by: { id: 'u-1', email: 'root@imc.test' },
      };
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

    it('offers only the roles strictly below the caller, never superadmin', () => {
      expect(signedInAs(['superadmin']).impersonatable).toEqual(['admin', 'operador', 'lector']);

      setActivePinia(createPinia());
      expect(signedInAs(['admin']).impersonatable).toEqual(['operador', 'lector']);

      setActivePinia(createPinia());
      expect(signedInAs(['operador']).impersonatable).toEqual(['lector']);
    });

    it('borrows a role: posts it, adopts the token and blocks every write', async () => {
      const own = superadminToken();
      localStorage.setItem(ACCESS_TOKEN_KEY, own);
      const auth = useAuthStore();
      expect(auth.isImpersonating).toBe(false);
      expect(auth.impersonationNotice).toBeNull();
      expect(auth.canWrite).toBe(true);

      const answer = impersonationAnswer('operador');
      vi.stubGlobal(
        'fetch',
        vi.fn((url: string, init?: RequestInit) => {
          calls.push({ url, init });
          return Promise.resolve(jsonResponse(answer));
        }),
      );

      await expect(auth.impersonate('operador')).resolves.toBe(true);

      expect(calls).toHaveLength(1);
      expect(calls[0]?.url).toContain('/users/impersonate');
      expect(calls[0]?.init?.method).toBe('POST');
      expect(postedBody(calls)).toEqual({ role: 'operador' });

      expect(auth.isImpersonating).toBe(true);
      expect(auth.impersonationNotice).toBe('Suplantando: rol operador');
      expect(auth.impersonatedRole).toBe('operador');
      expect(auth.token).toBe(answer.access_token);
      expect(localStorage.getItem(ACCESS_TOKEN_KEY)).toBe(answer.access_token);
      // Even the superadmin may not write while wearing somebody else's role.
      expect(auth.canWrite).toBe(false);
      expect(auth.canManageConfiguration).toBe(false);
      expect(auth.canManageUsers).toBe(false);
      // Nobody who already holds an impersonation token is offered the picker again.
      expect(auth.canImpersonate).toBe(false);
      expect(auth.impersonatable).toEqual([]);
      expect(auth.error).toBeNull();
    });

    it('refuses to impersonate again while a role is borrowed', async () => {
      localStorage.setItem(ACCESS_TOKEN_KEY, superadminToken());
      const auth = useAuthStore();
      vi.stubGlobal(
        'fetch',
        vi.fn((url: string, init?: RequestInit) => {
          calls.push({ url, init });
          return Promise.resolve(jsonResponse(impersonationAnswer('lector')));
        }),
      );

      await auth.impersonate('lector');
      await expect(auth.impersonate('lector')).resolves.toBe(false);

      // One request in total: the second attempt never leaves the browser.
      expect(calls).toHaveLength(1);
      expect(auth.error).toMatch(/Ya estás suplantando/);
      expect(auth.impersonationNotice).toBe('Suplantando: rol lector');
    });

    it('keeps the notice and the borrowed session across a reload', async () => {
      localStorage.setItem(ACCESS_TOKEN_KEY, superadminToken());
      const auth = useAuthStore();
      vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(jsonResponse(impersonationAnswer('admin')))));

      await auth.impersonate('admin');

      // A fresh store is what a page reload builds: same storage, new instance.
      setActivePinia(createPinia());
      const reloaded = useAuthStore();
      expect(reloaded.isImpersonating).toBe(true);
      expect(reloaded.impersonationNotice).toBe('Suplantando: rol admin');
      expect(reloaded.impersonatedBy?.email).toBe('root@imc.test');
      expect(reloaded.canWrite).toBe(false);
    });

    it('keeps the read-only notice even if the stored metadata was lost', () => {
      // Only the borrowed token survives: the role still comes from its own `act` claim.
      localStorage.setItem(ACCESS_TOKEN_KEY, borrowedToken('operador'));
      const auth = useAuthStore();

      expect(auth.isImpersonating).toBe(true);
      expect(auth.impersonationNotice).toBe('Suplantando: rol operador');
      expect(auth.canWrite).toBe(false);
      expect(auth.canImpersonate).toBe(false);
    });

    it('stop asks the API to close the impersonation and adopts the token it answers', async () => {
      const own = superadminToken();
      localStorage.setItem(ACCESS_TOKEN_KEY, own);
      const auth = useAuthStore();
      const borrowed = impersonationAnswer('operador');
      // What `POST /auth/stop-impersonation` answers: a fresh token for the caller's own account.
      const restored = makeToken({
        sub: 'u-1',
        email: 'root@imc.test',
        unique_name: 'root',
        role: ['superadmin'],
        exp: Math.floor(Date.now() / 1000) + 3600,
      });
      vi.stubGlobal(
        'fetch',
        vi.fn((url: string, init?: RequestInit) => {
          calls.push({ url, init });
          return Promise.resolve(
            url.includes('/auth/stop-impersonation')
              ? jsonResponse({ access_token: restored, expires_at_utc: null })
              : jsonResponse(borrowed),
          );
        }),
      );

      await auth.impersonate('operador');
      expect(auth.token).toBe(borrowed.access_token);

      await auth.stopImpersonation();

      // The borrowed token is the bearer the route expects: it goes out before anything is swapped.
      const stopCall = calls.find((call) => call.url.includes('/auth/stop-impersonation'));
      expect(stopCall?.init?.method).toBe('POST');
      expect((stopCall?.init?.headers as Record<string, string>).Authorization).toBe(
        `Bearer ${borrowed.access_token}`,
      );

      expect(auth.isImpersonating).toBe(false);
      expect(auth.impersonationNotice).toBeNull();
      expect(auth.token).toBe(restored);
      expect(localStorage.getItem(ACCESS_TOKEN_KEY)).toBe(restored);
      expect(localStorage.getItem('imc_role_impersonation')).toBeNull();
      expect(auth.roles).toEqual(['superadmin']);
      expect(auth.canWrite).toBe(true);
      expect(auth.canManageUsers).toBe(true);
      expect(auth.canImpersonate).toBe(true);
    });

    it('restores the stored own token when the API refuses to close the impersonation', async () => {
      const own = superadminToken();
      localStorage.setItem(ACCESS_TOKEN_KEY, own);
      const auth = useAuthStore();
      vi.stubGlobal(
        'fetch',
        vi.fn(() => Promise.resolve(jsonResponse(impersonationAnswer('operador')))),
      );
      await auth.impersonate('operador');
      expect(auth.isImpersonating).toBe(true);

      const refused: string[] = [];
      vi.stubGlobal(
        'fetch',
        vi.fn((url: string) => {
          refused.push(url);
          return Promise.resolve(emptyResponse(403));
        }),
      );

      await auth.stopImpersonation();

      // The call was made and refused: no audit row this time, but the banner comes down on the
      // caller's own session anyway.
      expect(refused).toHaveLength(1);
      expect(refused[0]).toContain('/auth/stop-impersonation');
      expect(auth.isImpersonating).toBe(false);
      expect(auth.impersonationNotice).toBeNull();
      expect(auth.token).toBe(own);
      expect(localStorage.getItem(ACCESS_TOKEN_KEY)).toBe(own);
      expect(auth.roles).toEqual(['superadmin']);
      expect(auth.canWrite).toBe(true);
    });

    it('ends the session when the stop fails and there is no own token to go back to', async () => {
      localStorage.setItem(ACCESS_TOKEN_KEY, borrowedToken('operador'));
      const auth = useAuthStore();
      expect(auth.isImpersonating).toBe(true);
      expect(auth.impersonationNotice).toBe('Suplantando: rol operador');

      const requested: string[] = [];
      const fetchMock = vi.fn((url: string) => {
        requested.push(url);
        return Promise.reject(new Error('offline'));
      });
      vi.stubGlobal('fetch', fetchMock);

      await auth.stopImpersonation();

      expect(fetchMock).toHaveBeenCalledTimes(1);
      expect(requested[0]).toContain('/auth/stop-impersonation');
      // With no own token to return to, the borrowed session ends instead of passing for a real one.
      expect(auth.isImpersonating).toBe(false);
      expect(auth.isAuthenticated).toBe(false);
      expect(localStorage.getItem(ACCESS_TOKEN_KEY)).toBeNull();
      expect(localStorage.getItem('imc_role_impersonation')).toBeNull();
    });

    it('never offers a lector the picker, and refuses the call without asking the API', async () => {
      const auth = signedInAs(['lector']);

      expect(auth.canImpersonate).toBe(false);
      expect(auth.impersonatable).toEqual([]);

      const fetchMock = vi.fn(() => Promise.resolve(jsonResponse({ role: 'lector' })));
      vi.stubGlobal('fetch', fetchMock);

      await expect(auth.impersonate('lector')).resolves.toBe(false);
      expect(fetchMock).not.toHaveBeenCalled();
      expect(auth.isImpersonating).toBe(false);
    });

    it('does not let the API widen the request: a role at or above the caller is not sent', async () => {
      const auth = signedInAs(['operador']);
      const fetchMock = vi.fn(() => Promise.resolve(jsonResponse({ role: 'admin' })));
      vi.stubGlobal('fetch', fetchMock);

      await expect(auth.impersonate('admin')).resolves.toBe(false);
      await expect(auth.impersonate('superadmin')).resolves.toBe(false);

      expect(fetchMock).not.toHaveBeenCalled();
      expect(auth.error).toMatch(/por debajo del tuyo/);
    });

    it('surfaces the API message when it refuses a role (400/403)', async () => {
      localStorage.setItem(ACCESS_TOKEN_KEY, superadminToken());
      const auth = useAuthStore();
      vi.stubGlobal(
        'fetch',
        vi.fn(() =>
          Promise.resolve(jsonResponse({ error: 'Target role must be below caller role.' }, 400)),
        ),
      );

      await expect(auth.impersonate('admin')).resolves.toBe(false);

      expect(auth.isImpersonating).toBe(false);
      expect(auth.error).toBe('Target role must be below caller role.');
      expect(auth.canWrite).toBe(true);
    });

    it('ends the impersonation when the borrowed token is refreshed, instead of bouncing to /login', async () => {
      localStorage.setItem(ACCESS_TOKEN_KEY, superadminToken());
      const auth = useAuthStore();
      vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(jsonResponse(impersonationAnswer('lector')))));
      await auth.impersonate('lector');

      const refreshed = superadminToken();
      const fetchMock = vi
        .fn()
        .mockResolvedValueOnce(emptyResponse(401)) // the borrowed token has expired (7 minutes)
        .mockResolvedValueOnce(jsonResponse({ access_token: refreshed })) // POST /auth/refresh
        .mockResolvedValueOnce(jsonResponse({ tree: [] })); // the retry
      vi.stubGlobal('fetch', fetchMock);

      await api.get('/item');

      // Impersonation is not renewable: the refresh cookie is the caller's own session, so the
      // session comes back as the caller. Re-installing the expired token is what made it 401 twice
      // and throw the user out to the login screen.
      expect(auth.token).toBe(refreshed);
      expect(auth.isImpersonating).toBe(false);
      expect(auth.impersonationNotice).toBeNull();
      expect(auth.canWrite).toBe(true);
      expect(localStorage.getItem(ACCESS_TOKEN_KEY)).toBe(refreshed);
    });

    it('ends the impersonation when the session is lost', async () => {
      localStorage.setItem(ACCESS_TOKEN_KEY, superadminToken());
      const auth = useAuthStore();
      vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(jsonResponse(impersonationAnswer('lector')))));
      await auth.impersonate('lector');

      vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(emptyResponse(401))));
      await api.get('/item').catch(() => undefined);

      expect(auth.isImpersonating).toBe(false);
      expect(auth.isAuthenticated).toBe(false);
      expect(localStorage.getItem('imc_role_impersonation')).toBeNull();
    });
  });
});
