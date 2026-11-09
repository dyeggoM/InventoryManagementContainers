import { useQuasar } from 'quasar';
import { getErrorMessage } from '../services/api';

/**
 * One place for user-facing feedback: every mutation reports success, every failure reports the
 * API's own message. Pages should not talk to Quasar notifications directly.
 */
export function useFeedback() {
  const $q = useQuasar();

  function success(message: string) {
    $q.notify({ type: 'positive', message, timeout: 2000 });
  }

  function error(err: unknown, fallback = 'Something went wrong') {
    const message = getErrorMessage(err) || fallback;
    $q.notify({
      type: 'negative',
      message,
      timeout: 6000,
      actions: [{ icon: 'close', color: 'white' }],
    });
  }

  function info(message: string) {
    $q.notify({ type: 'info', message, timeout: 2500 });
  }

  /** Resolves true only when the user explicitly confirms. */
  function confirmDelete(message: string): Promise<boolean> {
    return new Promise((resolve) => {
      let settled = false;
      const done = (value: boolean) => {
        if (!settled) {
          settled = true;
          resolve(value);
        }
      };
      $q.dialog({
        title: 'Confirm deletion',
        message,
        cancel: { label: 'Cancel', flat: true },
        ok: { label: 'Delete', color: 'negative', unelevated: true },
        persistent: true,
      })
        .onOk(() => done(true))
        .onCancel(() => done(false))
        .onDismiss(() => done(false));
    });
  }

  /** Resolves true when the user chooses to throw unsaved edits away. */
  function confirmDiscard(message = 'Your edits were not saved.'): Promise<boolean> {
    return new Promise((resolve) => {
      let settled = false;
      const done = (value: boolean) => {
        if (!settled) {
          settled = true;
          resolve(value);
        }
      };
      $q.dialog({
        title: 'Discard changes?',
        message,
        cancel: { label: 'Keep editing', flat: true },
        ok: { label: 'Discard', color: 'negative', unelevated: true },
        persistent: true,
      })
        .onOk(() => done(true))
        .onCancel(() => done(false))
        .onDismiss(() => done(false));
    });
  }

  return { success, error, info, confirmDelete, confirmDiscard };
}
