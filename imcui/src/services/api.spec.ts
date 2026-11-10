import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError, api, isApiPending, setLoginRedirect } from './api';

function jsonResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    statusText: String(status),
    json: () => Promise.resolve(body),
  } as unknown as Response;
}

describe('api transport', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.unstubAllGlobals();
    setLoginRedirect(() => undefined);
  });

  it('sends the token that is stored at request time, not at import time', async () => {
    localStorage.setItem('access_token', 'first');
    const requests: RequestInit[] = [];
    const fetchMock = vi.fn((_url: string, init?: RequestInit) => {
      requests.push(init ?? {});
      return Promise.resolve(jsonResponse({ ok: true }));
    });
    vi.stubGlobal('fetch', fetchMock);

    await api.get('/item');
    localStorage.setItem('access_token', 'second');
    await api.get('/item');

    const first = requests[0]?.headers as Record<string, string> | undefined;
    const second = requests[1]?.headers as Record<string, string> | undefined;

    expect(first?.Authorization).toBe('Bearer first');
    expect(second?.Authorization).toBe('Bearer second');
  });

  it('marks a 404 as a route the API has not built yet', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.resolve(jsonResponse({ title: 'Not Found' }, 404))));

    const error = await api.get('/users').catch((caught: unknown) => caught);

    expect(isApiPending(error)).toBe(true);
    expect(isApiPending(new ApiError(500, 'Server Error'))).toBe(false);
    expect(error).toBeInstanceOf(ApiError);
  });

  it('turns an unreachable API into a readable message instead of a bare TypeError', async () => {
    vi.stubGlobal('fetch', vi.fn(() => Promise.reject(new TypeError('Failed to fetch'))));

    const error = await api.get('/item').catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(0);
    expect((error as ApiError).message).toContain('Cannot reach the API');
  });

  it('returns nothing for a 204', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() =>
        Promise.resolve({
          ok: true,
          status: 204,
          statusText: 'No Content',
          json: () => Promise.reject(new Error('no body')),
        } as unknown as Response),
      ),
    );

    await expect(api.delete('/item/3')).resolves.toBeUndefined();
  });

  it('does not try to refresh when the login itself answers 401', async () => {
    const fetchMock = vi.fn(() => Promise.resolve(jsonResponse({ title: 'Unauthorized' }, 401)));
    vi.stubGlobal('fetch', fetchMock);

    await expect(api.post('/auth/login', { email: 'a', password: 'b' })).rejects.toBeInstanceOf(
      ApiError,
    );
    // One call: the credential is wrong, the session is not expired.
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });
});
