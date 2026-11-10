/**
 * Service worker registration, written against the platform instead of pulling in
 * `register-service-worker`.
 *
 * That package was imported here but never declared in `package.json`, so `vue-tsc --noEmit` failed on
 * a clean clone ("Cannot find module 'register-service-worker'") and a PWA build would have failed
 * too. What it provided was a thin wrapper over `navigator.serviceWorker.register` plus a few events,
 * which is less code than the dependency costs to declare, install and keep typed.
 *
 * Quasar injects `QUASAR_SERVICE_WORKER_FILE` when it builds the PWA; the fallback keeps a plain
 * `quasar build` working even when that variable is absent.
 */
const workerFile = (import.meta.env.QUASAR_SERVICE_WORKER_FILE as string | undefined) ?? 'service-worker.js';

export function registerServiceWorker(): void {
  if (typeof navigator === 'undefined' || !('serviceWorker' in navigator)) return;

  navigator.serviceWorker
    .register(workerFile)
    .then((registration) => {
      registration.addEventListener('updatefound', () => {
        const installing = registration.installing;
        if (!installing) return;

        installing.addEventListener('statechange', () => {
          // The first install also reaches `installed`; only an existing controller means the user was
          // already on an older version, which is the only moment worth interrupting them for.
          if (installing.state === 'installed' && navigator.serviceWorker.controller) {
            window.dispatchEvent(new CustomEvent('sw-updated'));
          }
        });
      });
    })
    .catch((error: unknown) => {
      console.error('Service worker registration failed:', error);
    });
}

registerServiceWorker();
