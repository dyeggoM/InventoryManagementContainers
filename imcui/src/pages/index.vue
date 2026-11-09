<template>
  <q-layout view="lHh Lpr lFf">
    <q-header elevated>
      <q-toolbar>
        <q-btn flat dense round icon="menu" aria-label="Menu" @click="toggleLeftDrawer" />

        <q-toolbar-title> IMCUI </q-toolbar-title>

        <div>Quasar v{{ $q.version }}</div>
      </q-toolbar>
    </q-header>

    <q-drawer v-model="leftDrawerOpen" bordered>
      <q-list>
        <q-item-label header> Links </q-item-label>

        <q-item>
          <q-item-section>
            <q-btn class="q-mt-md" color="primary" to="/" label="Go to Index Page" no-caps />
          </q-item-section>
        </q-item>
        <q-item>
          <q-item-section>
            <q-btn class="q-mt-md" color="primary" to="/second" label="Go to Second Page" no-caps />
          </q-item-section>
        </q-item>
        <q-item>
          <q-item-section>
            <q-btn class="q-mt-md" color="primary" to="/third" label="Go to Third Page" no-caps />
          </q-item-section>
        </q-item>
      </q-list>
      <!-- <q-list>
        <q-item-label header> Essential Links </q-item-label>

        <EssentialLink v-for="link in linksList" :key="link.label" v-bind="link" />
      </q-list> -->
    </q-drawer>

    <q-page-container>
      <router-view />
    </q-page-container>
  </q-layout>
</template>

<script setup lang="ts">
import { ref } from 'vue';
// import EssentialLink, { type EssentialLinkProps } from '@/components/EssentialLink.vue';

// const linksList: EssentialLinkProps[] = [
//   {
//     label: 'Second',
//     caption: 'second page',
//     icon: 'school',
//     link: '',
//   },
// ];

const leftDrawerOpen = ref(false);

function toggleLeftDrawer() {
  leftDrawerOpen.value = !leftDrawerOpen.value;
}
</script>
