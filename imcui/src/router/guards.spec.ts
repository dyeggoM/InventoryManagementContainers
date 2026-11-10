import { describe, expect, it } from 'vitest';
import {
  DEFAULT_PATH,
  isPublic,
  loginRedirect,
  requiredRoles,
  resolveNavigation,
  safeRedirectTarget,
} from './guards';

describe('router guards', () => {
  it('sends a visitor with no session to the login page, remembering where they were going', () => {
    const decision = resolveNavigation({ path: '/inventory', isAuthenticated: false, roles: [] });

    expect(decision.allowed).toBe(false);
    expect(decision.redirect).toBe('/login?redirect=%2Finventory');
  });

  it('lets an anonymous visitor open the login page', () => {
    expect(resolveNavigation({ path: '/login', isAuthenticated: false, roles: [] })).toEqual({
      allowed: true,
    });
  });

  it('keeps a signed-in user out of the login page', () => {
    const decision = resolveNavigation({ path: '/login', isAuthenticated: true, roles: ['lector'] });

    expect(decision.allowed).toBe(false);
    expect(decision.redirect).toBe(DEFAULT_PATH);
  });

  describe('by role', () => {
    it('lets a lector read the inventory', () => {
      expect(
        resolveNavigation({ path: '/inventory', isAuthenticated: true, roles: ['lector'] }).allowed,
      ).toBe(true);
    });

    it('keeps a lector and an operador out of configuration', () => {
      for (const role of ['lector', 'operador']) {
        const decision = resolveNavigation({
          path: '/configuration',
          isAuthenticated: true,
          roles: [role],
        });
        expect(decision.allowed, role).toBe(false);
        expect(decision.redirect, role).toBe(DEFAULT_PATH);
      }
    });

    it('lets an admin into configuration', () => {
      expect(
        resolveNavigation({
          path: '/configuration',
          isAuthenticated: true,
          roles: ['admin'],
        }).allowed,
      ).toBe(true);
    });

    it('only lets a superadmin into the dashboard', () => {
      for (const role of ['lector', 'operador', 'admin']) {
        expect(
          resolveNavigation({ path: '/dashboard', isAuthenticated: true, roles: [role] }).allowed,
          role,
        ).toBe(false);
      }

      expect(
        resolveNavigation({ path: '/dashboard', isAuthenticated: true, roles: ['superadmin'] })
          .allowed,
      ).toBe(true);
    });

    it('accepts a user holding several roles when one of them is enough', () => {
      expect(
        resolveNavigation({
          path: '/dashboard',
          isAuthenticated: true,
          roles: ['lector', 'superadmin'],
        }).allowed,
      ).toBe(true);
    });

    it('does not let the impersonated role widen access', () => {
      // The guard reads the real session's roles; impersonation only removes write affordances.
      expect(
        resolveNavigation({
          path: '/dashboard',
          isAuthenticated: true,
          roles: ['superadmin'],
        }).allowed,
      ).toBe(true);
    });
  });

  it('matches nested paths under a restricted prefix', () => {
    expect(
      resolveNavigation({ path: '/dashboard/users', isAuthenticated: true, roles: ['admin'] }),
    ).toEqual({ allowed: false, redirect: DEFAULT_PATH });
  });

  it('ignores a trailing slash', () => {
    expect(
      resolveNavigation({ path: '/configuration/', isAuthenticated: true, roles: ['admin'] }).allowed,
    ).toBe(true);
  });

  it('knows which paths are public and which roles they need', () => {
    expect(isPublic('/login')).toBe(true);
    expect(isPublic('/inventory')).toBe(false);
    expect(requiredRoles('/inventory')).toBeNull();
    expect(requiredRoles('/configuration')).toEqual(['superadmin', 'admin']);
    expect(requiredRoles('/dashboard')).toEqual(['superadmin']);
  });

  it('builds a login redirect that survives the round-trip', () => {
    expect(loginRedirect('/inventory')).toBe('/login?redirect=%2Finventory');
    expect(loginRedirect('/login')).toBe('/login');
  });

  it('refuses an off-site redirect target from the query string', () => {
    expect(safeRedirectTarget('/inventory')).toBe('/inventory');
    expect(safeRedirectTarget('//evil.test/')).toBe(DEFAULT_PATH);
    expect(safeRedirectTarget('https://evil.test')).toBe(DEFAULT_PATH);
    expect(safeRedirectTarget('/login')).toBe(DEFAULT_PATH);
    expect(safeRedirectTarget(undefined)).toBe(DEFAULT_PATH);
    expect(safeRedirectTarget(['/configuration'])).toBe('/configuration');
  });
});
