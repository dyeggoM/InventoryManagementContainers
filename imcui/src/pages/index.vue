<template>
  <q-layout view="lHh Lpr lFf">
    <!-- Permanent and fixed: while the impersonation lasts, it is visible on every page. -->
    <impersonation-banner />

    <q-header
      elevated
      class="app-header"
      :style="auth.isImpersonating ? { marginTop: '48px' } : undefined"
    >
      <q-toolbar>
        <q-btn
          flat
          dense
          round
          icon="menu"
          aria-label="Toggle navigation"
          @click="toggleLeftDrawer"
        />

        <q-toolbar-title>IMC · {{ currentSection.label }}</q-toolbar-title>

        <q-btn flat round dense icon="account_circle" aria-label="Cuenta y sesión">
          <q-menu anchor="bottom right" self="top right" auto-close>
            <q-list dense style="min-width: 240px">
              <q-item>
                <q-item-section>
                  <q-item-label class="text-weight-medium ellipsis">
                    {{ auth.email || 'Sin sesión' }}
                  </q-item-label>
                  <q-item-label caption>{{ rolesText }}</q-item-label>
                </q-item-section>
              </q-item>
              <q-separator />
              <q-item clickable v-close-popup @click="signOut">
                <q-item-section avatar>
                  <q-icon name="logout" />
                </q-item-section>
                <q-item-section>Cerrar sesión</q-item-section>
              </q-item>
            </q-list>
          </q-menu>
        </q-btn>
      </q-toolbar>
    </q-header>

    <q-drawer v-model="leftDrawerOpen" bordered show-if-above :width="240" :breakpoint="1023">
      <q-list padding>
        <q-item-label header>Inventory Management</q-item-label>
        <q-item
          v-for="section in sections"
          :key="section.to"
          clickable
          v-ripple
          :to="section.to"
          active-class="text-primary"
        >
          <q-item-section avatar>
            <q-icon :name="section.icon" />
          </q-item-section>
          <q-item-section>{{ section.label }}</q-item-section>
        </q-item>
      </q-list>
    </q-drawer>

    <q-page-container>
      <router-view />
    </q-page-container>
  </q-layout>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import ImpersonationBanner from '../components/ImpersonationBanner.vue';
import { roleLabel } from '../services/roles';
import { useAuthStore } from '../stores/auth-store';

interface Section {
  to: string;
  label: string;
  icon: string;
}

const auth = useAuthStore();
const router = useRouter();
const route = useRoute();

/**
 * The drawer only offers what the role can open. The router guard is the enforcement; this is the
 * same rule, so nobody is sent to a page that would bounce them straight back.
 */
const sections = computed<Section[]>(() => {
  const items: Section[] = [{ to: '/inventory', label: 'Inventory', icon: 'inventory_2' }];
  if (auth.isAdmin) items.push({ to: '/configuration', label: 'Configuration', icon: 'tune' });
  if (auth.isSuperadmin) items.push({ to: '/dashboard', label: 'Dashboard', icon: 'dashboard' });
  return items;
});

const fallback: Section = { to: '/inventory', label: 'Inventory', icon: 'inventory_2' };

const rolesText = computed(() => {
  if (auth.roles.length === 0) return 'Sin roles';
  return auth.roles.map((role) => roleLabel(role)).join(' · ');
});

const currentSection = computed(
  () => sections.value.find((section) => route.path.startsWith(section.to)) ?? fallback,
);

const leftDrawerOpen = ref(false);

function toggleLeftDrawer() {
  leftDrawerOpen.value = !leftDrawerOpen.value;
}

async function signOut(): Promise<void> {
  // The store revokes the refresh cookie and clears the token; navigating is this page's job.
  await auth.signOut();
  await router.replace('/login');
}
</script>
<style scoped>
/* Phones with a notch: keep the toolbar out from under the status bar. */
.app-header {
  padding-top: env(safe-area-inset-top);
}
</style>
