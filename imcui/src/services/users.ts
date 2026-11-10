import { api } from './api';
import { normalizeRoles, type Role } from './roles';
import { asText } from './utilities';

/**
 * The user management contract the dashboard is built against. The API does not expose these
 * routes yet: `GET/POST /api/users`, `PUT /api/users/{id}/roles`, `DELETE /api/users/{id}` are
 * agreed but unwritten, so every call is expected to answer 404 for now — the panel shows
 * "API pendiente" (see `isApiPending`) instead of an empty table that looks like a bug.
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
  password: string;
  roles: Role[];
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

export const usersService = {
  async list(): Promise<ManagedUser[]> {
    const response = await api.get<ManagedUser[] | UserListResponse>('/users');
    const rows = Array.isArray(response) ? response : (response?.items ?? []);
    return rows.map((row) => toUser(row as unknown as Record<string, unknown>));
  },

  create(request: CreateUserRequest): Promise<ManagedUser> {
    return api.post<ManagedUser>('/users', request);
  },

  /** Replaces the whole role set: a user with two roles keeps them both. */
  updateRoles(id: string, roles: Role[]): Promise<ManagedUser> {
    return api.put<ManagedUser>(`/users/${encodeURIComponent(id)}/roles`, { roles });
  },

  /** Deactivates the account; nothing is deleted from the audit trail. */
  deactivate(id: string): Promise<void> {
    return api.delete<void>(`/users/${encodeURIComponent(id)}`);
  },
};
