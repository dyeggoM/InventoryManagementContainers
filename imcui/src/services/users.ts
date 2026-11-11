import { api } from './api';
import { normalizeRoles, type Role } from './roles';
import { asText } from './utilities';

/**
 * The user management contract the dashboard is built against: `GET/POST /api/users`,
 * `PUT /api/users/{id}/roles`, `POST /api/users/{id}/password` and `DELETE /api/users/{id}`.
 * A route the API has not deployed still answers 404, and the panels show "API pendiente" (see
 * `isApiPending`) instead of an empty table that looks like a bug.
 */
export interface ManagedUser {
  id: string;
  email: string;
  roles: Role[];
  isActive: boolean;
  /** ISO timestamp of the last successful login, null when the account never signed in. */
  lastAccessUtc?: string | null;
}

export interface CreateUserRequest {
  email: string;
  /**
   * Optional: without it the API generates a temporary one and returns it once, in
   * `initialPassword`, so the caller can hand it over.
   */
  password?: string;
  roles: Role[];
}

/** `POST /api/users` answers the created account plus the password, once, when it generated one. */
export interface CreatedUser extends ManagedUser {
  initialPassword: string | null;
}

/** What `POST /api/users/impersonate` answers: a short-lived, read-only token for one role. */
export interface ImpersonationSession {
  access_token: string;
  expires_at_utc?: string | null;
  role: Role;
  scope: string;
  impersonated_by?: { id: string; email: string } | null;
}

/** The one-time password a reset may generate; null when the caller supplied its own. */
export interface PasswordResetResult {
  initialPassword: string | null;
}

/** The list arrives as-is or wrapped in `{ items }`; both are accepted, the UI wants an array. */
interface UserListResponse {
  items?: ManagedUser[];
}

function toUser(raw: Record<string, unknown>): ManagedUser {
  const id = raw.id;
  const lastAccess =
    typeof raw.lastAccessUtc === 'string'
      ? raw.lastAccessUtc
      : typeof raw.lastLoginUtc === 'string'
        ? raw.lastLoginUtc
        : null;
  return {
    id: typeof id === 'string' ? id : asText(id),
    email: asText(raw.email),
    roles: normalizeRoles(Array.isArray(raw.roles) ? raw.roles.map((role) => asText(role)) : []),
    isActive: raw.isActive === undefined ? true : Boolean(raw.isActive),
    lastAccessUtc: lastAccess,
  };
}

/** The API writes `null` for "you supplied the password": only a string is shown to the user. */
function initialPasswordOf(raw: Record<string, unknown>): string | null {
  const value = raw.initialPassword;
  return typeof value === 'string' && value.length > 0 ? value : null;
}

export const usersService = {
  async list(): Promise<ManagedUser[]> {
    const response = await api.get<ManagedUser[] | UserListResponse>('/users');
    const rows = Array.isArray(response) ? response : (response?.items ?? []);
    return rows.map((row) => toUser(row as unknown as Record<string, unknown>));
  },

  async create(request: CreateUserRequest): Promise<CreatedUser> {
    const created = await api.post<Record<string, unknown>>('/users', request);
    return { ...toUser(created), initialPassword: initialPasswordOf(created) };
  },

  /** Replaces the whole role set: a user with two roles keeps them both. */
  updateRoles(id: string, roles: Role[]): Promise<ManagedUser> {
    return api.put<ManagedUser>(`/users/${encodeURIComponent(id)}/roles`, { roles });
  },

  /**
   * Sets or resets a password (superadmin only). Without one the API generates it and answers
   * `initialPassword` once; either way the account's refresh tokens are revoked, so it has to sign
   * in again.
   */
  async resetPassword(id: string, password?: string): Promise<PasswordResetResult> {
    const body = password ? { password } : {};
    const response = await api.post<Record<string, unknown>>(
      `/users/${encodeURIComponent(id)}/password`,
      body,
    );
    return { initialPassword: initialPasswordOf(response ?? {}) };
  },

  /**
   * Borrows a role, never an account: the API answers a read-only token whose role sits strictly
   * below the caller's, and 400/403 with its own message when the request is not allowed.
   */
  async impersonate(role: Role): Promise<ImpersonationSession> {
    const response = await api.post<Record<string, unknown>>('/users/impersonate', { role });
    const returnedRole = normalizeRoles([asText(response?.role)])[0] ?? role;
    return {
      access_token: asText(response?.access_token),
      expires_at_utc:
        typeof response?.expires_at_utc === 'string' ? response.expires_at_utc : null,
      role: returnedRole,
      scope: asText(response?.scope),
      impersonated_by:
        response?.impersonated_by && typeof response.impersonated_by === 'object'
          ? (response.impersonated_by as { id: string; email: string })
          : null,
    };
  },

  /** Deactivates the account; nothing is deleted from the audit trail. */
  deactivate(id: string): Promise<void> {
    return api.delete<void>(`/users/${encodeURIComponent(id)}`);
  },
};
