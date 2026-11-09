<template>
  <q-layout view="lHh Lpr lFf">
    <q-header elevated>
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
      </q-toolbar>
    </q-header>

    <q-drawer v-model="leftDrawerOpen" bordered show-if-above :width="220">
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
import { useRoute } from 'vue-router';

interface Section {
  to: string;
  label: string;
  icon: string;
}

const sections: Section[] = [
  { to: '/inventory', label: 'Inventory', icon: 'inventory_2' },
  { to: '/configuration', label: 'Configuration', icon: 'tune' },
];

const fallback: Section = { to: '/inventory', label: 'Inventory', icon: 'inventory_2' };

const leftDrawerOpen = ref(false);
const route = useRoute();
const currentSection = computed(
  () => sections.find((section) => route.path.startsWith(section.to)) ?? fallback,
);

function toggleLeftDrawer() {
  leftDrawerOpen.value = !leftDrawerOpen.value;
}
</script>
