import { fileURLToPath } from 'node:url';
import vue from '@vitejs/plugin-vue';
import { defineConfig } from 'vitest/config';

/**
 * Unit tests for the parts of the UI that carry logic: the tree editor's state machine and the
 * attribute editor's typed rows. Components mount with Quasar's own components stubbed, so these
 * run without a browser and without the dev server.
 */
export default defineConfig({
  // Vitest carries its own copy of vite, so the plugin type of one is not the other's. The cast is
  // the price of a single config file; the runtime is the same code.
  plugins: [vue() as unknown as never],
  resolve: {
    alias: {
      '@services': fileURLToPath(new URL('./src/services', import.meta.url)),
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    include: ['src/**/*.spec.ts'],
    css: false,
  },
});
