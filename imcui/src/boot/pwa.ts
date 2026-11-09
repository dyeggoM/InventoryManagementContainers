import { Notify } from 'quasar';

/**
 * The service worker reports a new version through a window event; the boot file turns it into a
 * notification with one action. Reloading on its own would throw away a form the user is editing.
 */
export default function () {
  if (typeof window === 'undefined') return;

  window.addEventListener('sw-updated', () => {
    Notify.create({
      message: 'A new version is available.',
      color: 'primary',
      textColor: 'white',
      timeout: 0,
      position: 'top',
      actions: [
        { label: 'Reload', color: 'white', handler: () => window.location.reload() },
        { icon: 'close', color: 'white' },
      ],
    });
  });
}
