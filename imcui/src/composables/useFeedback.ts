import { useQuasar, type QDialogOptions } from 'quasar';
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

  function error(err: unknown, fallback = 'Algo ha salido mal') {
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
        title: 'Confirmar eliminación',
        message,
        cancel: { label: 'Cancelar', flat: true },
        ok: { label: 'Eliminar', color: 'negative', unelevated: true },
        persistent: true,
      })
        .onOk(() => done(true))
        .onCancel(() => done(false))
        .onDismiss(() => done(false));
    });
  }

  /**
   * Asks for one short answer, for the decisions that do not deserve a form of their own (the name
   * of a new set). Resolves the trimmed answer, or null when the user cancels or leaves it empty.
   */
  function prompt(title: string, label: string, message?: string): Promise<string | null> {
    return new Promise((resolve) => {
      let settled = false;
      const done = (value: string | null) => {
        if (!settled) {
          settled = true;
          resolve(value);
        }
      };
      const options: QDialogOptions = {
        title,
        prompt: { model: '', type: 'text', label },
        cancel: { label: 'Cancelar', flat: true },
        ok: { label: 'Crear', color: 'primary', unelevated: true },
        persistent: true,
      };
      if (message) options.message = message;
      $q.dialog(options)
        .onOk((value: string) => done((value ?? '').trim() || null))
        .onCancel(() => done(null))
        .onDismiss(() => done(null));
    });
  }

  /** Resolves true when the user chooses to throw unsaved edits away. */
  function confirmDiscard(message = 'Tus cambios no se guardaron.'): Promise<boolean> {
    return new Promise((resolve) => {
      let settled = false;
      const done = (value: boolean) => {
        if (!settled) {
          settled = true;
          resolve(value);
        }
      };
      $q.dialog({
        title: '¿Descartar los cambios?',
        message,
        cancel: { label: 'Seguir editando', flat: true },
        ok: { label: 'Descartar', color: 'negative', unelevated: true },
        persistent: true,
      })
        .onOk(() => done(true))
        .onCancel(() => done(false))
        .onDismiss(() => done(false));
    });
  }

  return { success, error, info, prompt, confirmDelete, confirmDiscard };
}
