/**
 * Where the access token lives, and what it says.
 *
 * Kept apart from `services/api.ts` and from the store so neither has to import the other: the
 * transport reads the token on every request, the store reads the claims the API signed, and
 * neither owns the other's state.
 */
import { normalizeRoles, type Role } from './roles';
import { asText } from './utilities';

/** The key the previous version of `api.ts` already read, so a stored session is not lost. */
export const ACCESS_TOKEN_KEY = 'access_token';

export interface SessionUser {
  id: string;
  email: string;
  name: string;
  roles: Role[];
}

/**
 * JWT claim names as they appear in the token the API issues (`AuthTokenService`): the outbound
 * mapping of `ClaimTypes` is the short JWT name, so `role` and `email` — the long URI forms are
 * accepted too, because a token minted by another handler still has to work.
 */
const EMAIL_CLAIMS = ['email', 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress'];
const NAME_CLAIMS = [
  'unique_name',
  'name',
  'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name',
];
const ID_CLAIMS = ['sub', 'nameid', 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier'];
const ROLE_CLAIMS = [
  'role',
  'roles',
  'http://schemas.microsoft.com/ws/2008/06/identity/claims/role',
];

/** `localStorage` throws when the page is embedded without an origin (and is absent in SSR). */
function storage(): Storage | null {
  try {
    return typeof localStorage === 'undefined' ? null : localStorage;
  } catch {
    return null;
  }
}

export function getStoredToken(): string | null {
  return storage()?.getItem(ACCESS_TOKEN_KEY) ?? null;
}

/** Writes the token, or forgets it when there is none. */
export function storeToken(token: string | null): void {
  const store = storage();
  if (!store) return;
  if (token) store.setItem(ACCESS_TOKEN_KEY, token);
  else store.removeItem(ACCESS_TOKEN_KEY);
}

export function clearStoredToken(): void {
  storeToken(null);
}

function decodeBase64Url(segment: string): string | null {
  try {
    const base64 = segment.replace(/-/g, '+').replace(/_/g, '/');
    const padded = base64.padEnd(base64.length + ((4 - (base64.length % 4)) % 4), '=');
    // atob yields one char per byte; the claims are JSON and the API writes no non-ASCII in them.
    return typeof atob === 'function' ? atob(padded) : null;
  } catch {
    return null;
  }
}

/** The claims of a token, or null when it is not a token this app can read. */
export function decodeTokenPayload(token: string): Record<string, unknown> | null {
  const segments = token.split('.');
  if (segments.length < 2) return null;
  const json = decodeBase64Url(segments[1] ?? '');
  if (!json) return null;
  try {
    const parsed: unknown = JSON.parse(json);
    return parsed && typeof parsed === 'object' ? (parsed as Record<string, unknown>) : null;
  } catch {
    return null;
  }
}

function claim(payload: Record<string, unknown>, names: readonly string[]): unknown {
  for (const name of names) {
    const value = payload[name];
    if (value !== undefined && value !== null && value !== '') return value;
  }
  return undefined;
}

function claimList(value: unknown): string[] {
  if (Array.isArray(value)) return value.map(String);
  if (typeof value === 'string') return value.split(/[,\s]+/).filter(Boolean);
  return [];
}

/**
 * The signed-in user, read from the token itself: the login response carries only
 * `{ access_token, expires_at_utc }`, and decoding is cheaper and less racy than a second request.
 */
export function decodeAccessToken(token: string): SessionUser | null {
  const payload = decodeTokenPayload(token);
  if (!payload) return null;

  const email = claim(payload, EMAIL_CLAIMS);
  const name = claim(payload, NAME_CLAIMS);
  const roles = normalizeRoles(claimList(claim(payload, ROLE_CLAIMS)));

  return {
    id: asText(claim(payload, ID_CLAIMS)),
    email: typeof email === 'string' ? email : '',
    name: typeof name === 'string' ? name : typeof email === 'string' ? email : '',
    roles,
  };
}

/**
 * An access token lives 15 minutes. Thirty seconds of slack keeps a request that leaves just
 * before the expiry from being rejected by the API's own skew window.
 */
export function isTokenExpired(token: string, skewSeconds = 30): boolean {
  const payload = decodeTokenPayload(token);
  const exp = payload?.exp;
  if (typeof exp !== 'number') return false; // no exp claim: let the API decide
  return exp * 1000 <= Date.now() + skewSeconds * 1000;
}
