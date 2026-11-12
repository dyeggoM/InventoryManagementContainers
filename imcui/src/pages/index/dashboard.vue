<template>
  <q-page class="q-pa-md imc-dashboard">
    <div class="row items-center q-gutter-sm q-mb-md">
      <q-icon name="dashboard" size="28px" color="primary" />
      <div class="column">
        <div class="text-h6">Dashboard</div>
        <div class="text-caption text-grey-7">Solo superadministradores</div>
      </div>
    </div>

    <q-banner v-if="auth.isImpersonating" dense class="bg-orange-1 text-orange-10 q-mb-md">
      <template v-slot:avatar><q-icon name="switch_account" /></template>
      {{ auth.impersonationNotice }}: las escrituras están bloqueadas.
      Usa el botón Salir del aviso superior para volver a tu sesión.
    </q-banner>

    <q-card flat bordered>
      <q-tabs
        v-model="tab"
        dense
        no-caps
        align="left"
        class="text-grey-8"
        active-color="primary"
        indicator-color="primary"
      >
        <q-tab name="users" icon="group" label="Usuarios" />
        <q-tab name="audit" icon="history" label="Auditoría" />
        <q-tab name="archive" icon="restore_from_trash" label="Archivo" />
        <q-tab name="impersonation" icon="switch_account" label="Suplantación" />
      </q-tabs>

      <q-separator />

      <q-tab-panels v-model="tab" animated>
        <q-tab-panel name="users" class="q-pa-md">
          <users-panel />
        </q-tab-panel>
        <q-tab-panel name="audit" class="q-pa-md">
          <audit-panel />
        </q-tab-panel>
        <q-tab-panel name="archive" class="q-pa-md">
          <archive-panel />
        </q-tab-panel>
        <q-tab-panel name="impersonation" class="q-pa-md">
          <impersonation-panel />
        </q-tab-panel>
      </q-tab-panels>
    </q-card>
  </q-page>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import UsersPanel from '../../components/dashboard/UsersPanel.vue';
import AuditPanel from '../../components/dashboard/AuditPanel.vue';
import ArchivePanel from '../../components/dashboard/ArchivePanel.vue';
import ImpersonationPanel from '../../components/dashboard/ImpersonationPanel.vue';
import { useAuthStore } from '../../stores/auth-store';

const auth = useAuthStore();
const tab = ref('users');
</script>

<style scoped>
.imc-dashboard {
  max-width: 1200px;
}
</style>
