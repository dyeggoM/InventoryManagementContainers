<template>
  <div class="impersonation-panel">
    <div class="row items-center q-gutter-sm q-mb-md">
      <div class="text-subtitle1 text-weight-medium">Suplantación</div>
      <q-space />
      <q-btn flat dense no-caps icon="refresh" label="Recargar" :loading="loading" @click="load" />
    </div>

    <q-banner v-if="pending" dense class="bg-orange-1 text-orange-10 q-mb-md">
      <template v-slot:avatar><q-icon name="construction" /></template>
      API pendiente: <code>GET /api/users</code> todavía no existe en la API, así que no hay a quién
      elegir. El selector y el aviso están listos para cuando responda.
    </q-banner>

    <q-banner v-else-if="errorMessage" dense class="bg-red-1 text-red-9 q-mb-md">
      <template v-slot:avatar><q-icon name="error_outline" /></template>
      {{ errorMessage }}
    </q-banner>

    <q-card v-if="auth.isImpersonating" flat bordered class="q-pa-md q-mb-md impersonating-card">
      <div class="row items-center q-gutter-sm">
        <q-icon name="switch_account" size="24px" color="orange-9" />
        <div class="column">
          <div class="text-body2">{{ auth.impersonationNotice }}</div>
          <div class="text-caption text-grey-7">
            Mientras dure, toda acción de escritura queda deshabilitada.
          </div>
        </div>
        <q-space />
        <q-btn
          unelevated
          no-caps
          color="orange-9"
          icon="logout"
          label="Salir"
          @click="stop"
        />
      </div>
    </q-card>

    <q-card flat bordered class="q-pa-md">
      <div class="text-body2 q-mb-sm">Elegir un usuario para ver la aplicación como él</div>
      <div class="row items-center q-gutter-sm">
        <q-select
          v-model="selectedId"
          dense
          outlined
          emit-value
          map-options
          class="col-12 col-sm-8"
          label="Usuario"
          :options="options"
          :loading="loading"
          :disable="auth.isImpersonating || users.length === 0"
          :hint="hint"
          options-dense
        />
        <q-btn
          unelevated
          no-caps
          color="primary"
          icon="switch_account"
          label="Suplantar"
          :disable="!selectedId || auth.isImpersonating"
          @click="start"
        />
      </div>
    </q-card>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { isApiPending } from '@services/api';
import { usersService, type ManagedUser } from '@services/users';
import { useAuthStore } from '../../stores/auth-store';

const auth = useAuthStore();

const users = ref<ManagedUser[]>([]);
const loading = ref(false);
const pending = ref(false);
const errorMessage = ref<string | null>(null);
const selectedId = ref<string | null>(null);

const options = computed(() =>
  users.value
    .filter((user) => user.isActive)
    .map((user) => ({ label: user.email, value: user.id })),
);

const hint = computed(() => {
  if (pending.value) return 'Sin datos: la API de usuarios todavía no existe.';
  if (options.value.length === 0) return 'No hay usuarios activos.';
  return 'Solo lectura: las escrituras quedan bloqueadas mientras suplantas.';
});

async function load(): Promise<void> {
  loading.value = true;
  pending.value = false;
  errorMessage.value = null;
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

function start(): void {
  const target = users.value.find((user) => user.id === selectedId.value);
  if (!target) return;
  auth.startImpersonation({
    id: target.id,
    email: target.email,
    name: target.email,
    roles: target.roles,
  });
}

function stop(): void {
  auth.stopImpersonation();
  selectedId.value = null;
}

onMounted(load);
</script>

<style scoped>
.impersonating-card {
  border-left: 4px solid var(--q-orange-9, #e65100);
  background: #fff8e1;
}
</style>
