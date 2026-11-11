import { beforeEach, describe, expect, it, vi } from 'vitest';
import { usersService } from './users';

function jsonResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    statusText: String(status),
    json: () => Promise.resolve(body),
  } as unknown as Response;
}

/** Records what the transport actually sends: the payload is the contract under test here. */
type Call = { url: string; init: RequestInit | undefined };

function stubApi(body: unknown, status = 200): Call[] {
  const calls: Call[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn((url: string, init?: RequestInit) => {
      calls.push({ url, init });
      return Promise.resolve(jsonResponse(body, status));
    }),
  );
  return calls;
}

function bodyOf(call: Call): unknown {
  const body = call.init?.body;
  return typeof body === 'string' ? JSON.parse(body) : undefined;
}

beforeEach(() => {
  localStorage.clear();
  vi.unstubAllGlobals();
});

describe('users service', () => {
  it('creates a user without a password: the API is asked to generate one', async () => {
    const calls = stubApi(
      {
        id: 'u-9',
        email: 'nuevo@imc.test',
        roles: ['lector'],
        isActive: true,
        lastAccessUtc: null,
        initialPassword: 'Temp-Pass-9!',
      },
      201,
    );

    const created = await usersService.create({ email: 'nuevo@imc.test', roles: ['lector'] });

    expect(calls).toHaveLength(1);
    expect(calls[0]?.url).toContain('/users');
    expect(calls[0]?.url).not.toContain('/password');
    // No `password` key at all: an empty string would be a password the API must accept.
    expect(bodyOf(calls[0]!)).toEqual({ email: 'nuevo@imc.test', roles: ['lector'] });
    expect(created.email).toBe('nuevo@imc.test');
    expect(created.initialPassword).toBe('Temp-Pass-9!');
  });

  it('creates a user with the password the caller supplied', async () => {
    const calls = stubApi(
      {
        id: 'u-10',
        email: 'jefe@imc.test',
        roles: ['operador'],
        isActive: true,
        lastAccessUtc: null,
        initialPassword: null,
      },
      201,
    );

    const created = await usersService.create({
      email: 'jefe@imc.test',
      password: 'Elegida-A-1!',
      roles: ['operador'],
    });

    expect(bodyOf(calls[0]!)).toEqual({
      email: 'jefe@imc.test',
      password: 'Elegida-A-1!',
      roles: ['operador'],
    });
    // The API answers null when it did not generate anything: nothing to hand over.
    expect(created.initialPassword).toBeNull();
  });

  it('resets a password without one and passes the generated one through', async () => {
    const calls = stubApi({ initialPassword: 'Generada-1!' });

    const result = await usersService.resetPassword('u-9');

    expect(calls[0]?.url).toContain('/users/u-9/password');
    expect(bodyOf(calls[0]!)).toEqual({});
    expect(result.initialPassword).toBe('Generada-1!');
  });

  it('resets a password to the one the caller chose', async () => {
    const calls = stubApi({ initialPassword: null });

    const result = await usersService.resetPassword('u-9', 'Nueva-2026!');

    expect(calls[0]?.url).toContain('/users/u-9/password');
    expect(bodyOf(calls[0]!)).toEqual({ password: 'Nueva-2026!' });
    expect(result.initialPassword).toBeNull();
  });

  it('escapes the id before it reaches the route', async () => {
    const calls = stubApi({ initialPassword: null });

    await usersService.resetPassword('u/9');

    expect(calls[0]?.url).toContain('/users/u%2F9/password');
  });

  it('impersonates by role and returns the session the API issued', async () => {
    const calls = stubApi({
      access_token: 'borrowed-token',
      expires_at_utc: '2026-11-11T10:00:00Z',
      role: 'operador',
      scope: 'read',
      impersonated_by: { id: 'u-1', email: 'root@imc.test' },
    });

    const session = await usersService.impersonate('operador');

    expect(calls[0]?.url).toContain('/users/impersonate');
    expect(calls[0]?.init?.method).toBe('POST');
    expect(bodyOf(calls[0]!)).toEqual({ role: 'operador' });
    expect(session.access_token).toBe('borrowed-token');
    expect(session.role).toBe('operador');
    expect(session.scope).toBe('read');
    expect(session.impersonated_by?.email).toBe('root@imc.test');
  });
});
