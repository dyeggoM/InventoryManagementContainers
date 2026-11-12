import { applyThemeMode, readStoredThemeMode } from '../stores/theme-store';

/**
 * Runs before the app mounts, so a reload in dark mode does not flash a white page first. The
 * applying is Quasar's (`Dark.set`), including `auto`, which starts following the OS preference
 * from here on; this file only decides *which* mode to apply.
 */
export default function () {
  applyThemeMode(readStoredThemeMode());
}
