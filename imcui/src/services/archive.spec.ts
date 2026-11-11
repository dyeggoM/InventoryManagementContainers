import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError, isApiPending } from './api';
import { archiveService, buildArchiveQuery, type ArchiveEntry } from './archive';

function jsonResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    statusText: String(status),
    json: () => Promise.resolve(body),
  } as unknown as Response;
}

function row(overrides: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    archiveId: 11,
    itemId: 7,
    label: 'Taladro',
    icon: 'construction',
    parentId: null,
    attributes: '{"Brand":"DeWalt"}',
    isActive: false,
    createdAt: '2026-11-01T09:00:00Z',
    createdBy: 'root@imc.test',
    updatedAt: null,
    updatedBy: null,
    deletedAtUtc: '2026-11-10T12:30:00Z',
    deletedById: 'user-1',
    deletedBy: 'root@imc.test',
    attachmentsJson: null,
    restoredAtUtc: null,
    restoredById: null,
    ...overrides,
  };
}

/** Stubs fetch and hands back every URL the service asked for. */
function captureFetch(
  body: unknown = { items: [], page: 1, pageSize: 25, total: 0 },
  status = 200,
) {
  const urls: { url: string; init?: RequestInit | undefined }[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn((url: string, init?: RequestInit) => {
      urls.push({ url, init });
      return Promise.resolve(jsonResponse(body, status));
    }),
  );
  return urls;
}

describe('archive query string', () => {
  it('leaves every unset filter out', () => {
    expect(buildArchiveQuery({})).toBe('');
    expect(buildArchiveQuery({ label: '  ', actor: null, from: null })).toBe('');
  });

  it('encodes every filled filter, in a stable order', () => {
    expect(
      buildArchiveQuery({
        page: 2,
        pageSize: 25,
        from: '2026-11-01',
        to: '2026-11-10',
        actor: 'root@imc.test',
        label: 'Taladro',
      }),
    ).toBe('?page=2&pageSize=25&from=2026-11-01&to=2026-11-10&actor=root%40imc.test&label=Taladro');
  });
});

describe('archiveService.list', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.unstubAllGlobals();
  });

  it('defaults to the first page of 25 and sends only the set filters', async () => {
    const urls = captureFetch();

    await archiveService.list({ label: 'Taladro' });

    expect(urls).toHaveLength(1);
    expect(urls[0]!.url).toBe(
      'http://localhost:5173/api/item/archive?page=1&pageSize=25&label=Taladro',
    );
    expect(urls[0]!.init?.method).toBe('GET');
  });

  it('carries the requested page and page size through the query string', async () => {
    const urls = captureFetch({ items: [], page: 3, pageSize: 50, total: 120 });

    const result = await archiveService.list({ page: 3, pageSize: 50 });

    expect(urls[0]!.url).toBe('http://localhost:5173/api/item/archive?page=3&pageSize=50');
    expect(result).toMatchObject({ page: 3, pageSize: 50, total: 120 });
  });

  it('maps a row to the shape the table reads, keeping a null attachments column null', async () => {
    captureFetch({
      items: [row(), row({ archiveId: 12, itemId: 8, attachmentsJson: '[{"id":1}]' })],
      page: 1,
      pageSize: 25,
      total: 2,
    });

    const result = await archiveService.list();
    const [first, second] = result.items as [ArchiveEntry, ArchiveEntry];

    expect(result.total).toBe(2);
    expect(first.archiveId).toBe(11);
    expect(first.itemId).toBe(7);
    expect(first.deletedBy).toBe('root@imc.test');
    expect(first.attachmentsJson).toBeNull();
    expect(first.restoredAtUtc).toBeNull();
    expect(second.attachmentsJson).toBe('[{"id":1}]');
  });

  it('falls back to what was asked for when the envelope omits page and pageSize', async () => {
    captureFetch({ items: [row()] });

    const result = await archiveService.list({ page: 2, pageSize: 10 });

    expect(result.items).toHaveLength(1);
    // The envelope said nothing: the request's own numbers are the truth, and total is the row count.
    expect(result).toMatchObject({ page: 2, pageSize: 10, total: 1 });
  });

  it('maps a 400 problem-details answer to the API message', async () => {
    captureFetch(
      { title: 'Invalid archive filters', detail: 'Use page >= 1, pageSize between 1 and 100.' },
      400,
    );

    const error = await archiveService.list({ page: 0 }).catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(400);
    expect((error as ApiError).message).toBe('Use page >= 1, pageSize between 1 and 100.');
  });
});

describe('archiveService.restore', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.unstubAllGlobals();
  });

  it('PATCHes the item activate route with no body and returns the restored item', async () => {
    const urls = captureFetch({ id: 7, label: 'Taladro', icon: 'construction', parentId: null });

    const restored = await archiveService.restore(7);

    expect(urls[0]!.url).toBe('http://localhost:5173/api/item/7/activate');
    expect(urls[0]!.init?.method).toBe('PATCH');
    expect(urls[0]!.init?.body).toBeUndefined();
    expect(restored).toMatchObject({ id: 7, label: 'Taladro' });
  });

  it('surfaces a 404 as an ApiError the panel can read as "already restored"', async () => {
    captureFetch({}, 404);

    const error = await archiveService.restore(7).catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(404);
    expect(isApiPending(error)).toBe(true);
  });
});
