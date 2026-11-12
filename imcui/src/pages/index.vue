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

        <!-- One button, three choices: the icon says which mode is on, the menu changes it. -->
        <q-btn flat round dense :icon="themeOption.icon" :aria-label="themeAriaLabel">
          <q-menu anchor="bottom right" self="top right" auto-close>
            <q-list dense style="min-width: 200px">
              <q-item
                v-for="option in themeOptions"
                :key="option.mode"
                clickable
                v-close-popup
                :active="theme.mode === option.mode"
                active-class="text-primary"
                :aria-label="option.label"
                @click="theme.setMode(option.mode)"
              >
                <q-item-section avatar>
                  <q-icon :name="option.icon" />
                </q-item-section>
                <q-item-section>{{ option.label }}</q-item-section>
                <q-item-section side>
                  <q-icon v-if="theme.mode === option.mode" name="check" aria-hidden="true" />
                </q-item-section>
              </q-item>
            </q-list>
          </q-menu>
        </q-btn>

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
import { useThemeStore, type ThemeMode } from '../stores/theme-store';

interface Section {
  to: string;
  label: string;
  icon: string;
}

interface ThemeOption {
  mode: ThemeMode;
  label: string;
  icon: string;
}

const auth = useAuthStore();
const theme = useThemeStore();
const router = useRouter();
const route = useRoute();

/** The three states, in the order an operator expects them: follow the system, force light, force dark. */
const THEME_OPTIONS: Record<ThemeMode, ThemeOption> = {
  auto: { mode: 'auto', label: 'Sistema', icon: 'brightness_auto' },
  light: { mode: 'light', label: 'Claro', icon: 'light_mode' },
  dark: { mode: 'dark', label: 'Oscuro', icon: 'dark_mode' },
};

/** Insertion order, which is the order above: system first, then the two forced modes. */
const themeOptions = Object.values(THEME_OPTIONS);

/** What the button shows and what the menu ticks: both follow the mode the store holds. */
const themeOption = computed(() => THEME_OPTIONS[theme.mode]);

/** Says which mode is on, because the icon alone does not read out to a screen reader. */
const themeAriaLabel = computed(() => `Tema: ${themeOption.value.label}`);

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
