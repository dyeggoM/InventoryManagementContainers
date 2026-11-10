import { acceptHMRUpdate, defineStore } from 'pinia';
import { computed, ref } from 'vue';
import { ApiError, api, getErrorMessage, onSessionEvent } from '../services/api';
import { ADMIN_ROLES, WRITE_ROLES, hasAnyRole, type Role } from '../services/roles';
import {
  decodeAccessToken,
  getStoredToken,
  isTokenExpired,
  storeToken,
  type SessionUser,
} from '../services/session';

/** What `POST /auth/login` and `POST /auth/refresh` answer (see `AuthController`). */
interface TokenResponse {
  access_token: string;
  expires_at_utc?: string;
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
  const impersonatedUser = ref<SessionUser | null>(null);
  const loading = ref(false);
  const error = ref<string | null>(null);

  const roles = computed<Role[]>(() => user.value?.roles ?? []);
  const email = computed(() => user.value?.email ?? '');
  const displayName = computed(() => user.value?.name || user.value?.email || '');
  const isAuthenticated = computed(() => token.value !== null && user.value !== null);
  /** True when the token's own `exp` has passed: the transport can still trade it for a new one. */
  const tokenExpired = computed(() => (token.value ? isTokenExpired(token.value) : true));

  const isImpersonating = computed(() => impersonatedUser.value !== null);
  const impersonationNotice = computed(() =>
    impersonatedUser.value ? `Estás suplantando a ${impersonatedUser.value.email}` : null,
  );

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

  function applyToken(value: string | null): void {
    token.value = value;
    user.value = value ? decodeAccessToken(value) : null;
    storeToken(value);
    if (!value) impersonatedUser.value = null; // a lost session ends any impersonation with it
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

  function startImpersonation(target: SessionUser): void {
    impersonatedUser.value = target;
  }

  function stopImpersonation(): void {
    impersonatedUser.value = null;
  }

  // The transport refreshes and expires sessions on its own (see services/api.ts). Mirroring those
  // events keeps the header, the banner and the guards from showing a session that no longer is.
  onSessionEvent((event) => {
    if (event.type === 'refreshed') applyToken(event.token);
    else applyToken(null);
  });

  return {
    token,
    user,
    impersonatedUser,
    loading,
    error,
    roles,
    email,
    displayName,
    isAuthenticated,
    tokenExpired,
    isImpersonating,
    impersonationNotice,
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
    startImpersonation,
    stopImpersonation,
  };
});

if (import.meta.hot) {
  import.meta.hot.accept(acceptHMRUpdate(useAuthStore, import.meta.hot));
}
