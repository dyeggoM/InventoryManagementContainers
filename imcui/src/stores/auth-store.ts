import { acceptHMRUpdate, defineStore } from 'pinia';
import { computed, ref } from 'vue';
import { ApiError, api, getErrorMessage, onSessionEvent } from '../services/api';
import {
  ADMIN_ROLES,
  WRITE_ROLES,
  hasAnyRole,
  impersonatableRoles,
  isRole,
  type Role,
} from '../services/roles';
import {
  decodeAccessToken,
  decodeTokenPayload,
  getStoredToken,
  isTokenExpired,
  storeToken,
  type SessionUser,
} from '../services/session';
import { usersService } from '../services/users';

/** What `POST /auth/login` and `POST /auth/refresh` answer (see `AuthController`). */
interface TokenResponse {
  access_token: string;
  expires_at_utc?: string;
}

/**
 * The impersonation survives a reload: the stored token is the borrowed one, so the notice and the
 * read-only behaviour have to come back with it. The token it replaced is kept here, because
 * re-issuing it is the only way back to the caller's own session.
 */
const IMPERSONATION_KEY = 'imc_role_impersonation';

interface StoredImpersonation {
  role: Role;
  actor: { id: string; email: string } | null;
  /** The caller's own token, kept so `stopImpersonation` can hand it back; null if it was lost. */
  originalToken: string | null;
}

function readStoredImpersonation(): StoredImpersonation | null {
  try {
    const raw = typeof localStorage === 'undefined' ? null : localStorage.getItem(IMPERSONATION_KEY);
    if (!raw) return null;
    const parsed: unknown = JSON.parse(raw);
    if (!parsed || typeof parsed !== 'object') return null;
    const record = parsed as Record<string, unknown>;
    const role = record.role;
    const originalToken = record.originalToken;
    if (typeof role !== 'string' || !isRole(role)) return null;
    const actor = record.actor as StoredImpersonation['actor'];
    return {
      role,
      actor: actor ?? null,
      originalToken: typeof originalToken === 'string' ? originalToken : null,
    };
  } catch {
    return null;
  }
}

function writeStoredImpersonation(value: StoredImpersonation | null): void {
  try {
    if (typeof localStorage === 'undefined') return;
    if (value?.originalToken) localStorage.setItem(IMPERSONATION_KEY, JSON.stringify(value));
    else localStorage.removeItem(IMPERSONATION_KEY);
  } catch {
    // Private mode: the session still works, it just does not survive a reload.
  }
}

/** The API signs impersonation tokens with an `act` claim carrying the caller's id. */
function isImpersonationToken(token: string): boolean {
  const claim = decodeTokenPayload(token)?.act;
  return typeof claim === 'string' && claim.length > 0;
}

/**
 * The one place that knows who is signed in. Everything else — router guards, the layout, the
 * write affordances — asks this store instead of reading localStorage or the token itself.
 */
export const useAuthStore = defineStore('auth', () => {
  // The token is the source of truth and it is persisted; the user is read back from it, so a
  // reload restores the session without a round-trip.
  const token = ref<string | null>(getStoredToken());
  const user = ref<SessionUser | null>(token.value ? decodeAccessToken(token.value) : null);
  const loading = ref(false);
  const error = ref<string | null>(null);

  // A borrowed token is identified by its own `act` claim, not by what was persisted: a token that
  // was rotated or replaced behind the store's back must not leave a stale notice on screen. If the
  // metadata is gone the role still comes from the token, so the read-only affordances stay honest.
  const stored = readStoredImpersonation();
  const borrowedRole =
    token.value !== null && isImpersonationToken(token.value)
      ? (stored?.role ?? user.value?.roles[0] ?? null)
      : null;
  const borrowed: StoredImpersonation | null = borrowedRole
    ? { role: borrowedRole, actor: stored?.actor ?? null, originalToken: stored?.originalToken ?? null }
    : null;
  const impersonatedRole = ref<Role | null>(borrowed?.role ?? null);
  const impersonatedBy = ref<{ id: string; email: string } | null>(borrowed?.actor ?? null);
  const impersonationOriginalToken = ref<string | null>(borrowed?.originalToken ?? null);

  const roles = computed<Role[]>(() => user.value?.roles ?? []);
  const email = computed(() => user.value?.email ?? '');
  const displayName = computed(() => user.value?.name || user.value?.email || '');
  const isAuthenticated = computed(() => token.value !== null && user.value !== null);
  /** True when the token's own `exp` has passed: the transport can still trade it for a new one. */
  const tokenExpired = computed(() => (token.value ? isTokenExpired(token.value) : true));

  const isImpersonating = computed(() => impersonatedRole.value !== null);
  const impersonationNotice = computed(() =>
    impersonatedRole.value ? `Suplantando: rol ${impersonatedRole.value}` : null,
  );
  /** The roles the caller may borrow: strictly below their own, never `superadmin`. */
  const impersonatable = computed<Role[]>(() =>
    isImpersonating.value ? [] : impersonatableRoles(roles.value),
  );
  const canImpersonate = computed(() => impersonatable.value.length > 0);

  const isSuperadmin = computed(() => roles.value.includes('superadmin'));
  const isAdmin = computed(() => hasAnyRole(roles.value, ADMIN_ROLES));
  const isReader = computed(() => roles.value.includes('lector'));

  /**
   * Writing — fiches, attachments, users, roles — is what the operator and up may do. While a
   * superadmin impersonates somebody, nothing may be written: the session is borrowed for reading.
   */
  const canWrite = computed(() => !isImpersonating.value && hasAnyRole(roles.value, WRITE_ROLES));
  const canManageConfiguration = computed(
    () => !isImpersonating.value && hasAnyRole(roles.value, ADMIN_ROLES),
  );
  const canManageUsers = computed(() => !isImpersonating.value && isSuperadmin.value);

  function clearImpersonation(): void {
    impersonatedRole.value = null;
    impersonatedBy.value = null;
    impersonationOriginalToken.value = null;
    writeStoredImpersonation(null);
  }

  function applyToken(value: string | null): void {
    token.value = value;
    user.value = value ? decodeAccessToken(value) : null;
    storeToken(value);
    if (!value) clearImpersonation(); // a lost session ends any impersonation with it
  }

  function hasRole(...wanted: string[]): boolean {
    return hasAnyRole(roles.value, wanted);
  }

  async function login(emailAddress: string, password: string): Promise<boolean> {
    loading.value = true;
    error.value = null;
    try {
      const response = await api.post<TokenResponse>(
        '/auth/login',
        {
          email: emailAddress,
          password,
        },
        // The login response is what sets the HttpOnly refresh cookie. Without credentials the
        // browser throws that Set-Cookie away on a cross-origin call, and the silent refresh can
        // never work: the access token expires and the user is sent back to the login form.
        { credentials: 'include' },
      );
      if (!response?.access_token) {
        error.value = 'La API no devolvió un token de acceso.';
        return false;
      }
      applyToken(response.access_token);
      return true;
    } catch (caught) {
      error.value =
        caught instanceof ApiError && caught.status === 401
          ? 'Correo o contraseña incorrectos.'
          : getErrorMessage(caught);
      return false;
    } finally {
      loading.value = false;
    }
  }

  /**
   * Trades the refresh cookie for a new access token. Returns false (and leaves the store signed
   * out) when the cookie is gone too.
   */
  async function refresh(): Promise<boolean> {
    try {
      const response = await api.post<TokenResponse>('/auth/refresh', undefined, {
        credentials: 'include',
      });
      if (!response?.access_token) {
        applyToken(null);
        return false;
      }
      applyToken(response.access_token);
      return true;
    } catch {
      applyToken(null);
      return false;
    }
  }

  async function logout(): Promise<void> {
    try {
      // The API revokes the cookie's token; the cookie itself is what it is deleting.
      await api.post<void>('/auth/logout', undefined, { credentials: 'include' });
    } catch {
      // An unreachable API must not keep somebody signed in on this device.
    }
  }

  /** Signs out locally too: the caller navigates once this resolves. */
  async function signOut(): Promise<void> {
    await logout();
    applyToken(null);
  }

  /**
   * Borrows a role, never an account: the API answers a read-only token for a role strictly below
   * the caller's. The caller's own token is kept aside so `stopImpersonation` can hand it back.
   * Returns false (and leaves `error` with the server's own message) when the API refuses.
   */
  async function impersonate(role: Role): Promise<boolean> {
    error.value = null;
    if (isImpersonating.value) {
      error.value = 'Ya estás suplantando un rol: sal de esa suplantación antes de entrar en otra.';
      return false;
    }
    if (!impersonatable.value.includes(role)) {
      error.value = 'Solo puedes entrar como un rol estrictamente por debajo del tuyo.';
      return false;
    }
    const original = token.value;
    if (!original) {
      error.value = 'No hay una sesión que suplantar.';
      return false;
    }
    loading.value = true;
    try {
      const session = await usersService.impersonate(role);
      if (!session.access_token) {
        error.value = 'La API no devolvió un token de suplantación.';
        return false;
      }
      impersonatedRole.value = session.role;
      impersonatedBy.value = session.impersonated_by ?? null;
      impersonationOriginalToken.value = original;
      writeStoredImpersonation({
        role: session.role,
        actor: impersonatedBy.value,
        originalToken: original,
      });
      applyToken(session.access_token);
      return true;
    } catch (caught) {
      error.value = getErrorMessage(caught);
      return false;
    } finally {
      loading.value = false;
    }
  }

  /**
   * Returns to the caller's own session through the API, so the end of the impersonation is
   * audited: `POST /auth/stop-impersonation` writes the `ImpersonateEnd` row and answers a fresh
   * token for the caller's account. The borrowed token stays in place for the call — it is the
   * bearer that route expects — and is only replaced once the answer arrives.
   *
   * A refused or unreachable call means no audit row, never a stuck banner: the caller's token that
   * `impersonate` kept aside is the fallback, and without it the session ends, because a borrowed
   * token left behind would look like a real one.
   */
  async function stopImpersonation(): Promise<void> {
    const original = impersonationOriginalToken.value;
    let handedBack: string | null = null;
    loading.value = true;
    try {
      const response = await api.post<TokenResponse>('/auth/stop-impersonation');
      handedBack = response?.access_token ?? null;
    } catch {
      // Network failure, 401 (a seven-minute borrow already expired) or 403: fall back below.
    } finally {
      loading.value = false;
    }
    clearImpersonation();
    applyToken(handedBack ?? original ?? null);
  }

  // The transport refreshes and expires sessions on its own (see services/api.ts). Mirroring those
  // events keeps the header, the banner and the guards from showing a session that no longer is.
  onSessionEvent((event) => {
    if (event.type === 'refreshed') {
      // Impersonation is deliberately not renewable: the borrowed token lives seven minutes, and the
      // refresh cookie belongs to the caller's real session, so the token it returns carries the full
      // roles of the account that asked. Re-installing the borrowed token here is what turned that
      // limit into a bounce to /login — the expired token went out again, 401'd, and the session was
      // discarded. Adopting the refreshed token ends the impersonation instead, and the banner and the
      // read-only state go with it.
      if (isImpersonating.value) clearImpersonation();
      applyToken(event.token);
    } else applyToken(null);
  });

  return {
    token,
    user,
    impersonatedRole,
    impersonatedBy,
    loading,
    error,
    roles,
    email,
    displayName,
    isAuthenticated,
    tokenExpired,
    isImpersonating,
    impersonationNotice,
    impersonatable,
    canImpersonate,
    isSuperadmin,
    isAdmin,
    isReader,
    canWrite,
    canManageConfiguration,
    canManageUsers,
    hasRole,
    login,
    refresh,
    logout,
    signOut,
    impersonate,
    stopImpersonation,
  };
});

if (import.meta.hot) {
  import.meta.hot.accept(acceptHMRUpdate(useAuthStore, import.meta.hot));
}
