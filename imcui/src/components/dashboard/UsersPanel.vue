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
      API pendiente: <code>GET /api/users</code> todavía no existe en la API. La pantalla está
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
            label="Contraseña inicial"
            type="password"
            outlined
            dense
            autocomplete="new-password"
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
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue';
import { isApiPending } from '@services/api';
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
      errorMessage.value = error instanceof Error ? error.message : String(error);
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
  if (!draft.email.trim() || !draft.password) {
    createError.value = 'El correo y la contraseña inicial son obligatorios.';
    return;
  }
  creating.value = true;
  try {
    await usersService.create({
      email: draft.email.trim(),
      password: draft.password,
      roles: [draft.role],
    });
    showCreate.value = false;
    await load();
  } catch (error) {
    createError.value = isApiPending(error)
      ? 'API pendiente: POST /api/users todavía no existe.'
      : error instanceof Error
        ? error.message
        : String(error);
  } finally {
    creating.value = false;
  }
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
      ? 'API pendiente: PUT /api/users/{id}/roles todavía no existe.'
      : error instanceof Error
        ? error.message
        : String(error);
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
      ? 'API pendiente: DELETE /api/users/{id} todavía no existe.'
      : error instanceof Error
        ? error.message
        : String(error);
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
</style>
