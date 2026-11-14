/**
 * The roles the API authorizes (`Program.cs` policies `RequiereLectura` / `RequiereEscritura` /
 * `RequiereAdmin` / `RequiereSuperadmin`). The UI mirrors them here — one list, so the router
 * guard, the store and the screens cannot drift apart.
 */
export const ROLES = ['superadmin', 'admin', 'operador', 'lector'] as const;

export type Role = (typeof ROLES)[number];

export const ROLE_LABELS: Record<Role, string> = {
  superadmin: 'Superadministrador',
  admin: 'Administrador',
  operador: 'Operador',
  lector: 'Lector',
};

/** Any role may read. */
export const READ_ROLES: readonly Role[] = ROLES;
/** Fiches and attachments. */
export const WRITE_ROLES: readonly Role[] = ['superadmin', 'admin', 'operador'];
/** The configuration tree and the user list. */
export const ADMIN_ROLES: readonly Role[] = ['superadmin', 'admin'];
/** Users, the audit trail and the archive: the panels only a superadmin may see. */
export const SUPERADMIN_ROLES: readonly Role[] = ['superadmin'];

/**
 * The impersonation hierarchy, highest first. The API accepts a target only when it sits strictly
 * below the caller's own role (`UserController.Impersonate`) and never lets `superadmin` be
 * borrowed, so the picker and the store derive their options from this one list.
 */
export const ROLE_HIERARCHY: readonly Role[] = ['superadmin', 'admin', 'operador', 'lector'];

/**
 * The roles the signed-in user may enter as: strictly below their own, never their own and never
 * `superadmin`. A lector gets an empty list, which is how the panel knows not to offer the picker.
 */
export function impersonatableRoles(callerRoles: readonly Role[]): Role[] {
  const own = ROLE_HIERARCHY.find((role) => callerRoles.includes(role));
  if (!own) return [];
  return ROLE_HIERARCHY.slice(ROLE_HIERARCHY.indexOf(own) + 1).filter(
    (role) => role !== 'superadmin',
  );
}

/** A role the UI knows; anything else is kept out of the store rather than rendered half-broken. */
export function isRole(value: string): value is Role {
  return (ROLES as readonly string[]).includes(value);
}

/**
 * Claims come back lower-cased from the API's seeder, but a hand-edited database can carry case
 * or a role the UI does not know yet. Normalise, deduplicate and drop the unknown ones.
 */
export function normalizeRoles(raw: readonly string[] | undefined | null): Role[] {
  const seen = new Set<Role>();
  for (const value of raw ?? []) {
    const role = String(value).trim().toLowerCase();
    if (isRole(role)) seen.add(role);
  }
  // Keep the declared order so a table always reads superadmin, admin, operador, lector.
  return ROLES.filter((role) => seen.has(role));
}

/** The human label for a stored role; unknown values are shown as they are, not swallowed. */
export function roleLabel(role: string): string {
  const normalized = role.toLowerCase();
  return isRole(normalized) ? ROLE_LABELS[normalized] : role;
}

/** True when the user holds at least one of the roles the screen requires. */
export function hasAnyRole(roles: readonly string[], wanted: readonly string[]): boolean {
  return wanted.some((role) => roles.includes(role));
}
