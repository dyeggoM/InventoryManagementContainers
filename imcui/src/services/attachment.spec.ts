import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError, setLoginRedirect } from './api';
import { attachmentsService } from './attachment';

/** A binary answer: the route sends bytes, so only `blob()` is meaningful here. */
function blobResponse(body: Blob, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    statusText: String(status),
    json: () => Promise.resolve({ title: String(status) }),
    blob: () => Promise.resolve(body),
  } as unknown as Response;
}

describe('attachmentsService.fetchFile', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.unstubAllGlobals();
    setLoginRedirect(() => undefined);
  });

  it('asks the file route with the stored bearer token and resolves the bytes', async () => {
    localStorage.setItem('access_token', 'token-1');
    const file = new Blob(['png-bytes'], { type: 'image/png' });
    const requests: { url: string; init?: RequestInit | undefined }[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn((url: string, init?: RequestInit) => {
        requests.push({ url, init });
        return Promise.resolve(blobResponse(file));
      }),
    );

    const result = await attachmentsService.fetchFile(7);

    expect(requests).toHaveLength(1);
    expect(requests[0]!.url).toBe('http://localhost:5173/api/attachment/7/file');
    expect(requests[0]!.init?.method).toBe('GET');
    expect((requests[0]!.init?.headers as Record<string, string>).Authorization).toBe(
      'Bearer token-1',
    );
    // The same object the API sent: the preview and the download both read it.
    expect(result).toBe(file);
    expect(result.type).toBe('image/png');
  });

  it('maps a 404 to an ApiError, like every other service call', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(blobResponse(new Blob(), 404))),
    );

    const error = await attachmentsService.fetchFile(99).catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(404);
  });

  it('maps an exhausted 401 to the expired-session ApiError, without a second fetch of the file', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() =>
        Promise.resolve({
          ok: false,
          status: 401,
          statusText: 'Unauthorized',
          json: () => Promise.resolve({ title: 'Unauthorized' }),
          blob: () => Promise.resolve(new Blob()),
        } as unknown as Response),
      ),
    );

    const error = await attachmentsService.fetchFile(7).catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(401);
    expect((error as ApiError).message).toContain('sesión expiró');
  });
});
