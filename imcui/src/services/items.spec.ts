import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from './api';
import { itemsService, type Item } from './items';

function jsonResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    statusText: String(status),
    json: () => Promise.resolve(body),
  } as unknown as Response;
}

/** Stubs fetch and hands back every call the service made, in order. */
function captureFetch(body: unknown = {}, status = 200) {
  const requests: { url: string; init?: RequestInit | undefined }[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn((url: string, init?: RequestInit) => {
      requests.push({ url, init });
      return Promise.resolve(jsonResponse(body, status));
    }),
  );
  return requests;
}

function item(id: number, label: string, children: Item[] = []): Item {
  return { id, label, icon: '', children, parentId: null };
}

describe('itemsService.attachChildren', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.unstubAllGlobals();
  });

  it('POSTs the whole list to the parent route and answers how many moved', async () => {
    localStorage.setItem('access_token', 'token-1');
    const requests = captureFetch({ moved: 3 });

    const result = await itemsService.attachChildren(5, [7, 8, 9]);

    expect(requests).toHaveLength(1);
    expect(requests[0]!.url).toBe('http://localhost:5173/api/item/5/children');
    expect(requests[0]!.init?.method).toBe('POST');
    // One request for the lot: a set of six pieces is not six round-trips.
    expect(JSON.parse(requests[0]!.init?.body as string)).toEqual({ ids: [7, 8, 9] });
    expect((requests[0]!.init?.headers as Record<string, string>).Authorization).toBe(
      'Bearer token-1',
    );
    expect(result.moved).toBe(3);
  });

  it('maps a 400 (the parent, a repeated id or a cycle) to the API message', async () => {
    captureFetch({ title: 'Invalid hierarchy', detail: 'Child item 5 would create a cycle.' }, 400);

    const error = await itemsService.attachChildren(5, [5]).catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(400);
    expect((error as ApiError).message).toBe('Child item 5 would create a cycle.');
  });

  it('maps a 404 (not in this organization) and a 403 (no write permission)', async () => {
    captureFetch({ title: 'Not Found' }, 404);
    const notFound = await itemsService.attachChildren(5, [99]).catch((caught: unknown) => caught);
    expect(notFound).toBeInstanceOf(ApiError);
    expect((notFound as ApiError).status).toBe(404);

    vi.unstubAllGlobals();
    captureFetch({ title: 'Forbidden' }, 403);
    const forbidden = await itemsService.attachChildren(5, [7]).catch((caught: unknown) => caught);
    expect(forbidden).toBeInstanceOf(ApiError);
    expect((forbidden as ApiError).status).toBe(403);
  });
});

describe('itemsService.detachChild', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.unstubAllGlobals();
  });

  it('DELETEs the child route and resolves nothing on the 204', async () => {
    const requests = captureFetch(undefined, 204);

    await expect(itemsService.detachChild(5, 8)).resolves.toBeUndefined();

    expect(requests[0]!.url).toBe('http://localhost:5173/api/item/5/children/8');
    expect(requests[0]!.init?.method).toBe('DELETE');
  });

  it('maps a 404 (the child is not under that parent) to an ApiError', async () => {
    captureFetch({ title: 'Not Found' }, 404);

    const error = await itemsService.detachChild(5, 8).catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(404);
  });
});

describe('itemsService.findItem', () => {
  it('finds a node by id at any depth and reports an id that is not there', () => {
    const tree = [item(1, 'BACKLINE', [item(2, 'STAND', [item(3, 'CLAMP')])]), item(4, 'DRUMS')];

    expect(itemsService.findItem(tree, 3)?.label).toBe('CLAMP');
    expect(itemsService.findItem(tree, 4)?.label).toBe('DRUMS');
    expect(itemsService.findItem(tree, 99)).toBeUndefined();
  });
});

describe('itemsService.rootCandidates', () => {
  it('offers the roots only, and never the set itself', () => {
    const tree = [
      item(5, 'Hardware set', [item(7, 'Stand')]),
      item(8, 'Base de brazo'),
      item(9, 'Strap'),
    ];

    const candidates = itemsService.rootCandidates(tree, 5);

    // A piece already in a set is not offered: attaching it would move it out of its own set.
    expect(candidates.map((candidate) => candidate.label)).toEqual(['Base de brazo', 'Strap']);
  });
});
