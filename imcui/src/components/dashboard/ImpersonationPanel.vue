<template>
  <div class="impersonation-panel">
    <div class="row items-center q-gutter-sm q-mb-md">
      <div class="text-subtitle1 text-weight-medium">Suplantación</div>
    </div>

    <q-banner v-if="errorMessage" dense class="bg-red-1 text-red-9 q-mb-md">
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
          :loading="stopping"
          :disable="stopping"
          @click="stop"
        />
      </div>
    </q-card>

    <q-banner
      v-else-if="!auth.canImpersonate"
      dense
      class="bg-blue-grey-1 text-blue-grey-9 q-mb-md"
    >
      <template v-slot:avatar><q-icon name="info" /></template>
      Tu rol no permite suplantar a nadie: solo se entra como un rol estrictamente por debajo del
      propio.
    </q-banner>

    <q-card v-else flat bordered class="q-pa-md">
      <div class="text-body2 q-mb-sm">Entrar como un rol por debajo del tuyo</div>
      <div class="text-caption text-grey-7 q-mb-sm">{{ caption }}</div>
      <div class="row items-center q-gutter-sm">
        <q-select
          v-model="selectedRole"
          dense
          outlined
          emit-value
          map-options
          class="col-12 col-sm-8"
          label="Rol"
          :options="roleOptions"
          :disable="!auth.canImpersonate"
          hint="Solo lectura: las escrituras quedan bloqueadas mientras suplantas."
          options-dense
        />
        <q-btn
          unelevated
          no-caps
          color="primary"
          icon="switch_account"
          label="Suplantar"
          :disable="!selectedRole || !auth.canImpersonate"
          :loading="starting"
          @click="start"
        />
      </div>
    </q-card>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue';
import type { Role } from '@services/roles';
import { useAuthStore } from '../../stores/auth-store';

const auth = useAuthStore();

const selectedRole = ref<Role | null>(null);
const starting = ref(false);
const stopping = ref(false);
const errorMessage = ref<string | null>(null);

/** Offered in hierarchy order, highest first: exactly what the API accepts from this caller. */
const roleOptions = computed(() =>
  auth.impersonatable.map((role) => ({ label: role, value: role })),
);

/** The hierarchy in one line: "puedes entrar como admin, operador o lector". */
const caption = computed(() => {
  const roles = auth.impersonatable;
  if (roles.length === 0) return 'No hay ningún rol por debajo del tuyo.';
  const list =
    roles.length === 1
      ? roles[0]
      : `${roles.slice(0, -1).join(', ')} o ${roles[roles.length - 1]}`;
  return `Puedes entrar como ${list}.`;
});

async function start(): Promise<void> {
  errorMessage.value = null;
  const role = selectedRole.value;
  if (!role) return;
  starting.value = true;
  try {
    const entered = await auth.impersonate(role);
    // The store keeps the API's own message when it refuses (400/403): show that, not a guess.
    if (!entered) errorMessage.value = auth.error ?? 'No se pudo iniciar la suplantación.';
    else selectedRole.value = null;
  } finally {
    starting.value = false;
  }
}

/**
 * Leaving is a round-trip — the API writes the `ImpersonateEnd` audit row — so the picker is only
 * reset once the store has actually swapped the token back.
 */
async function stop(): Promise<void> {
  if (stopping.value) return;
  stopping.value = true;
  try {
    await auth.stopImpersonation();
    selectedRole.value = null;
    errorMessage.value = null;
  } finally {
    stopping.value = false;
  }
}
</script>

<style scoped>
.impersonating-card {
  border-left: 4px solid var(--q-orange-9, #e65100);
  background: #fff8e1;
}
</style>
