<template>
  <div class="users-panel">
    <div class="row items-center q-gutter-sm q-mb-md">
      <div class="text-subtitle1 text-weight-medium">Usuarios</div>
      <q-space />
      <q-btn
        flat
        dense
        no-caps
        icon="refresh"
        label="Recargar"
        :loading="loading"
        @click="load"
      />
      <q-btn
        unelevated
        no-caps
        color="primary"
        icon="person_add"
        label="Nuevo usuario"
        :disable="!auth.canManageUsers"
        @click="openCreate"
      />
    </div>

    <!-- The route does not exist yet: say so, loudly, instead of showing an empty table. -->
    <q-banner v-if="pending" dense class="bg-orange-1 text-orange-10 q-mb-md">
      <template v-slot:avatar><q-icon name="construction" /></template>
      API pendiente: <code>GET /api/users</code> todavía no responde en esta API. La pantalla está
      lista y se llenará sola cuando el endpoint responda.
    </q-banner>

    <q-banner v-else-if="errorMessage" dense class="bg-red-1 text-red-9 q-mb-md">
      <template v-slot:avatar><q-icon name="error_outline" /></template>
      {{ errorMessage }}
    </q-banner>

    <q-table
      :rows="users"
      :columns="columns"
      row-key="id"
      dense
      flat bordered
      :loading="loading"
      :rows-per-page-options="[0]"
      no-data-label="Todavía no hay usuarios para mostrar"
    >
      <template v-slot:body-cell-email="props">
        <q-td :props="props">
          <span class="text-body2">{{ props.row.email }}</span>
        </q-td>
      </template>

      <template v-slot:body-cell-roles="props">
        <q-td :props="props">
          <q-select
            dense
            borderless
            multiple
            use-chips
            emit-value
            map-options
            class="role-select"
            :model-value="props.row.roles"
            :options="roleOptions"
            :disable="!auth.canManageUsers"
            :loading="busyId === props.row.id"
            :aria-label="`Roles de ${props.row.email}`"
            @update:model-value="(value: string[]) => changeRoles(props.row, value)"
          />
        </q-td>
      </template>

      <template v-slot:body-cell-isActive="props">
        <q-td :props="props">
          <q-badge :color="props.row.isActive ? 'positive' : 'grey-6'" rounded>
            {{ props.row.isActive ? 'Activo' : 'Inactivo' }}
          </q-badge>
        </q-td>
      </template>

      <template v-slot:body-cell-lastAccessUtc="props">
        <q-td :props="props">{{ formatTimestamp(props.row.lastAccessUtc) }}</q-td>
      </template>

      <template v-slot:body-cell-actions="props">
        <q-td :props="props" class="text-right">
          <q-btn
            flat
            dense
            round
            no-caps
            icon="key"
            color="primary"
            :aria-label="`Restablecer contraseña de ${props.row.email}`"
            :disable="!auth.canManageUsers"
            @click="openReset(props.row)"
          >
            <q-tooltip>Restablecer contraseña</q-tooltip>
          </q-btn>
          <q-btn
            flat
            dense
            round
            no-caps
            icon="person_off"
            color="negative"
            :aria-label="`Desactivar ${props.row.email}`"
            :disable="!auth.canManageUsers || !props.row.isActive"
            :loading="busyId === props.row.id"
            @click="deactivate(props.row)"
          >
            <q-tooltip>Desactivar</q-tooltip>
          </q-btn>
        </q-td>
      </template>
    </q-table>

    <q-dialog v-model="showCreate" persistent>
      <q-card class="q-pa-md create-card">
        <div class="text-subtitle1 q-mb-md">Nuevo usuario</div>

        <q-form @submit="create" class="column q-gutter-md">
          <q-input v-model="draft.email" label="Correo" type="email" outlined dense autofocus />
          <q-input
            v-model="draft.password"
            label="Contraseña (opcional)"
            type="password"
            outlined
            dense
            autocomplete="new-password"
            hint="Déjala vacía y la API genera una temporal."
          />
          <q-select
            v-model="draft.role"
            label="Rol"
            outlined
            dense
            emit-value
            map-options
            :options="roleOptions"
          />

          <div v-if="createError" class="text-negative text-caption">{{ createError }}</div>

          <div class="row justify-end q-gutter-sm">
            <q-btn flat no-caps label="Cancelar" type="button" @click="showCreate = false" />
            <q-btn
              unelevated
              no-caps
              color="primary"
              label="Crear"
              type="submit"
              :loading="creating"
            />
          </div>
        </q-form>
      </q-card>
    </q-dialog>

    <q-dialog v-model="showReset" persistent>
      <q-card class="q-pa-md create-card">
        <div class="text-subtitle1 q-mb-md">Restablecer contraseña</div>
        <div class="text-body2 q-mb-sm">{{ resetTarget?.email }}</div>

        <q-form @submit="submitReset" class="column q-gutter-md">
          <q-input
            v-model="resetDraft"
            label="Contraseña (opcional)"
            type="password"
            outlined
            dense
            autocomplete="new-password"
            hint="Déjala vacía y la API genera una temporal."
          />
          <div class="text-caption text-grey-7">
            Al restablecerla se cierran todas las sesiones activas de esa cuenta.
          </div>

          <div v-if="resetError" class="text-negative text-caption">{{ resetError }}</div>

          <div class="row justify-end q-gutter-sm">
            <q-btn flat no-caps label="Cancelar" type="button" @click="showReset = false" />
            <q-btn
              unelevated
              no-caps
              color="primary"
              label="Restablecer"
              type="submit"
              :loading="resetting"
            />
          </div>
        </q-form>
      </q-card>
    </q-dialog>

    <q-dialog v-model="showPassword">
      <q-card class="q-pa-md create-card">
        <div class="text-subtitle1 q-mb-sm">Contraseña temporal</div>
        <div class="text-body2 q-mb-sm">{{ passwordFor }}</div>

        <div class="password-value q-mb-md">{{ passwordValue }}</div>

        <q-banner dense class="bg-orange-1 text-orange-10 q-mb-md">
          <template v-slot:avatar><q-icon name="warning_amber" /></template>
          Se muestra solo una vez: cópiala ahora y entrégasela a la persona. La API no la vuelve a
          enviar.
        </q-banner>

        <div class="row justify-end q-gutter-sm">
          <q-btn
            flat
            no-caps
            icon="content_copy"
            :label="copied ? 'Copiado' : 'Copiar'"
            @click="copyPassword"
          />
          <q-btn unelevated no-caps color="primary" label="Entendido" @click="closePassword" />
        </div>
      </q-card>
    </q-dialog>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue';
import { getErrorMessage, isApiPending } from '@services/api';
import { ROLES, ROLE_LABELS, type Role } from '@services/roles';
import { usersService, type ManagedUser } from '@services/users';
import { formatTimestamp } from '@services/utilities';
import { useAuthStore } from '../../stores/auth-store';

const auth = useAuthStore();

const users = ref<ManagedUser[]>([]);
const loading = ref(false);
const pending = ref(false);
const errorMessage = ref<string | null>(null);
const busyId = ref<string | null>(null);

const showCreate = ref(false);
const creating = ref(false);
const createError = ref<string | null>(null);
const draft = reactive<{ email: string; password: string; role: Role }>({
  email: '',
  password: '',
  role: 'lector',
});

const showReset = ref(false);
const resetting = ref(false);
const resetError = ref<string | null>(null);
const resetTarget = ref<ManagedUser | null>(null);
const resetDraft = ref('');

// The API generates a password when the caller does not supply one and answers it exactly once:
// it is kept in memory only, and the dialog is the only place it is ever shown.
const showPassword = ref(false);
const passwordValue = ref('');
const passwordFor = ref('');
const copied = ref(false);

const roleOptions = ROLES.map((role) => ({ label: ROLE_LABELS[role], value: role }));

const columns = [
  { name: 'email', label: 'Correo', field: 'email', align: 'left' as const, sortable: true },
  { name: 'roles', label: 'Roles', field: 'roles', align: 'left' as const },
  { name: 'isActive', label: 'Activo', field: 'isActive', align: 'left' as const },
  { name: 'lastAccessUtc', label: 'Último acceso', field: 'lastAccessUtc', align: 'left' as const },
  { name: 'actions', label: '', field: 'id', align: 'right' as const },
];

async function load(): Promise<void> {
  loading.value = true;
  errorMessage.value = null;
  pending.value = false;
  try {
    users.value = await usersService.list();
  } catch (error) {
    if (isApiPending(error)) {
      pending.value = true;
      users.value = [];
    } else {
      errorMessage.value = getErrorMessage(error);
    }
  } finally {
    loading.value = false;
  }
}

function openCreate(): void {
  draft.email = '';
  draft.password = '';
  draft.role = 'lector';
  createError.value = null;
  showCreate.value = true;
}

async function create(): Promise<void> {
  createError.value = null;
  const email = draft.email.trim();
  if (!email) {
    createError.value = 'El correo es obligatorio.';
    return;
  }
  creating.value = true;
  try {
    const created = await usersService.create({
      email,
      roles: [draft.role],
      // An empty field is not an empty password: it means "let the API generate a temporary one".
      ...(draft.password ? { password: draft.password } : {}),
    });
    showCreate.value = false;
    await load();
    if (created.initialPassword) {
      showInitialPassword(created.initialPassword, created.email || email);
    }
  } catch (error) {
    createError.value = isApiPending(error)
      ? 'API pendiente: POST /api/users todavía no responde.'
      : getErrorMessage(error);
  } finally {
    creating.value = false;
  }
}

function openReset(user: ManagedUser): void {
  resetTarget.value = user;
  resetDraft.value = '';
  resetError.value = null;
  showReset.value = true;
}

async function submitReset(): Promise<void> {
  const target = resetTarget.value;
  if (!target) return;
  resetError.value = null;
  resetting.value = true;
  try {
    const result = await usersService.resetPassword(target.id, resetDraft.value || undefined);
    showReset.value = false;
    if (result.initialPassword) showInitialPassword(result.initialPassword, target.email);
  } catch (error) {
    resetError.value = isApiPending(error)
      ? 'API pendiente: POST /api/users/{id}/password todavía no responde.'
      : getErrorMessage(error);
  } finally {
    resetting.value = false;
  }
}

function showInitialPassword(value: string, email: string): void {
  passwordValue.value = value;
  passwordFor.value = email;
  copied.value = false;
  showPassword.value = true;
}

async function copyPassword(): Promise<void> {
  try {
    await navigator.clipboard?.writeText(passwordValue.value);
    copied.value = true;
  } catch {
    // No clipboard permission (or no clipboard at all): the value stays on screen to copy by hand.
    copied.value = false;
  }
}

function closePassword(): void {
  showPassword.value = false;
  passwordValue.value = '';
  passwordFor.value = '';
}

async function changeRoles(user: ManagedUser, roles: string[]): Promise<void> {
  busyId.value = user.id;
  errorMessage.value = null;
  try {
    const updated = await usersService.updateRoles(user.id, roles as Role[]);
    // The API is the authority on the resulting role set; fall back to what was sent.
    user.roles = updated?.roles ?? (roles as Role[]);
  } catch (error) {
    errorMessage.value = isApiPending(error)
      ? 'API pendiente: PUT /api/users/{id}/roles todavía no responde.'
      : getErrorMessage(error);
  } finally {
    busyId.value = null;
  }
}

async function deactivate(user: ManagedUser): Promise<void> {
  busyId.value = user.id;
  errorMessage.value = null;
  try {
    await usersService.deactivate(user.id);
    user.isActive = false;
  } catch (error) {
    errorMessage.value = isApiPending(error)
      ? 'API pendiente: DELETE /api/users/{id} todavía no responde.'
      : getErrorMessage(error);
  } finally {
    busyId.value = null;
  }
}

onMounted(load);
</script>

<style scoped>
.role-select {
  min-width: 180px;
}

.create-card {
  width: 100%;
  max-width: 420px;
}

.password-value {
  font-family: 'Fira Code', 'Courier New', monospace;
  font-size: 1rem;
  letter-spacing: 0.02em;
  word-break: break-all;
  background: var(--imc-surface-sunken);
  border-radius: 4px;
  padding: 8px 12px;
}
</style>
