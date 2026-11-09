<template>
  <q-page class="flex flex-center">
    <q-spinner color="primary" size="2em" />
  </q-page>
</template>

<script setup lang="ts">
import { useRouter } from 'vue-router';

// The landing route only forwards to the inventory; there is nothing to show at "/".
const router = useRouter();
void router.replace('/inventory');
</script>
