<template>
  <!-- Permanent: it stays while the impersonation lasts, on every page, whatever is scrolled. -->
  <div v-if="auth.isImpersonating" class="impersonation-banner" role="status" aria-live="polite">
    <q-icon name="switch_account" size="20px" />
    <span class="impersonation-banner__text">{{ auth.impersonationNotice }}</span>
    <q-btn
      dense
      no-caps
      unelevated
      color="white"
      text-color="orange-10"
      icon="logout"
      label="Salir"
      class="q-ml-sm"
      @click="stop"
    />
  </div>
</template>

<script setup lang="ts">
import { useAuthStore } from '../stores/auth-store';

const auth = useAuthStore();

function stop(): void {
  auth.stopImpersonation();
}
</script>

<style scoped>
.impersonation-banner {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  /* Above the header and the drawers: the warning must never be hidden by the app chrome. */
  z-index: 3000;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  padding: 8px 12px calc(8px + env(safe-area-inset-bottom, 0px));
  background: #e65100;
  color: #fff;
  font-size: 14px;
  font-weight: 500;
  box-shadow: 0 2px 6px rgba(0, 0, 0, 0.25);
}

.impersonation-banner__text {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
