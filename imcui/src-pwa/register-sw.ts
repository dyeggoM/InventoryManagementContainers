import { register } from 'register-service-worker';

/**
 * Registered by Quasar when the app is built in PWA mode. The only event worth interrupting the user
 * for is a new version: an installed app can sit open for days, and a reload is the moment the
 * service worker takes over. Everything else stays quiet.
 */
register(import.meta.env.QUASAR_SERVICE_WORKER_FILE, {
  updated(/* registration */) {
    // The first install also reports "updated"; only an existing controller means a real upgrade.
    if (!navigator.serviceWorker?.controller) return;
    window.dispatchEvent(new CustomEvent('sw-updated'));
  },

  offline() {
    window.dispatchEvent(new CustomEvent('sw-offline'));
  },

  error(err) {
    console.error('Service worker registration failed:', err);
  },
});
