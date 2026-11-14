/**
 * Navigation rules, kept as a pure function so they can be exercised without a router, a Pinia
 * instance or a network. `router/index.ts` is a thin adapter over `resolveNavigation`.
 */
import { ADMIN_ROLES, hasAnyRole } from '../services/roles';

export const LOGIN_PATH = '/login';
/** Where a signed-in visitor lands when the page asked for does not exist for them. */
export const DEFAULT_PATH = '/inventory';

export interface GuardContext {
  /** `route.path` with no query string. */
  path: string;
  isAuthenticated: boolean;
  roles: readonly string[];
  /** Somebody already signed in who opens /login goes here instead. */
  fallback?: string;
}

export interface GuardDecision {
  allowed: boolean;
  /** Set when `allowed` is false: the path to navigate to instead. */
  redirect?: string;
}

/** The routes that demand a role beyond "signed in". Order matters only for prefixes. */
const RESTRICTED: readonly { prefix: string; roles: readonly string[] }[] = [
  // The dashboard holds the import and the impersonation picker, both admin screens; the panels
  // that are superadmin-only (users, audit, archive) hide themselves inside it instead.
  { prefix: '/dashboard', roles: ADMIN_ROLES },
  { prefix: '/configuration', roles: ADMIN_ROLES },
];

function normalizePath(path: string): string {
  const withoutQuery = path.split('?')[0] ?? '';
  if (withoutQuery.length > 1 && withoutQuery.endsWith('/')) {
    return withoutQuery.replace(/\/+$/, '') || '/';
  }
  return withoutQuery || '/';
}

/** Everything but the login page needs a session. */
export function isPublic(path: string): boolean {
  return normalizePath(path) === LOGIN_PATH;
}

/** The roles a path demands, or null when being signed in is enough. */
export function requiredRoles(path: string): readonly string[] | null {
  const clean = normalizePath(path);
  const match = RESTRICTED.find(
    (entry) => clean === entry.prefix || clean.startsWith(`${entry.prefix}/`),
  );
  return match?.roles ?? null;
}

/** The login URL that remembers where the visitor was going. */
export function loginRedirect(path: string): string {
  const clean = normalizePath(path);
  if (isPublic(clean) || clean === '/') return LOGIN_PATH;
  return `${LOGIN_PATH}?redirect=${encodeURIComponent(clean)}`;
}

/**
 * Decides whether a navigation may proceed. Signed out → login. A role-gated page with the wrong
 * role → the default page (never the login page: the session is valid, the role is not).
 */
export function resolveNavigation(context: GuardContext): GuardDecision {
  const path = normalizePath(context.path);
  const fallback = context.fallback ?? DEFAULT_PATH;

  if (isPublic(path)) {
    return context.isAuthenticated ? { allowed: false, redirect: fallback } : { allowed: true };
  }

  if (!context.isAuthenticated) {
    return { allowed: false, redirect: loginRedirect(path) };
  }

  const needed = requiredRoles(path);
  if (needed && !hasAnyRole(context.roles, needed)) {
    return { allowed: false, redirect: fallback };
  }

  return { allowed: true };
}

/** Only a same-origin absolute path is accepted as a `?redirect=` target. */
export function safeRedirectTarget(value: unknown, fallback = DEFAULT_PATH): string {
  const raw = Array.isArray(value) ? value[0] : value;
  if (typeof raw !== 'string' || !raw.startsWith('/') || raw.startsWith('//')) return fallback;
  return isPublic(raw) ? fallback : raw;
}
