import { acceptHMRUpdate, defineStore } from 'pinia';
import { computed, ref } from 'vue';
import { Dark } from 'quasar';

/**
 * What the header offers. `auto` is not a third palette: it hands the decision to the operating
 * system and keeps following it while the app is open.
 */
export type ThemeMode = 'auto' | 'light' | 'dark';

/** One key, one value: the choice survives a reload on its own, with no session involved. */
const THEME_KEY = 'imc_theme';

const THEME_MODES: readonly ThemeMode[] = ['auto', 'light', 'dark'];

export function isThemeMode(value: unknown): value is ThemeMode {
  return typeof value === 'string' && (THEME_MODES as readonly string[]).includes(value);
}

/**
 * The stored choice, or `auto` when there is nothing usable: a key written by an older build, a
 * hand-edited value or a storage that refuses to be read must not stop the app from starting.
 */
export function readStoredThemeMode(): ThemeMode {
  try {
    const raw = typeof localStorage === 'undefined' ? null : localStorage.getItem(THEME_KEY);
    return isThemeMode(raw) ? raw : 'auto';
  } catch {
    return 'auto';
  }
}

function persistThemeMode(mode: ThemeMode): void {
  try {
    if (typeof localStorage === 'undefined') return;
    localStorage.setItem(THEME_KEY, mode);
  } catch {
    // Private mode: the choice still applies, it just does not survive a reload.
  }
}

/**
 * Applying is Quasar's job, not ours: `Dark.set('auto')` is what reads `prefers-color-scheme`, what
 * keeps listening to it, and what flips `body--dark`. Re-implementing the media query here would
 * give the app two opinions about which mode is on.
 */
export function applyThemeMode(mode: ThemeMode): void {
  Dark.set(mode === 'auto' ? 'auto' : mode === 'dark');
}

/**
 * The one place that knows how the app should look. The header reads `mode` to show which choice is
 * active; anything that needs "is it dark right now" asks `isDark`, which is Quasar's own answer.
 */
export const useThemeStore = defineStore('theme', () => {
  const mode = ref<ThemeMode>(readStoredThemeMode());

  // Applied at creation too, not only from the boot file: a store built before the boot file ran
  // (or in a test) must already be in the right mode.
  applyThemeMode(mode.value);

  /** True while Quasar is painting dark — following the OS too, when the mode is `auto`. */
  const isDark = computed(() => Dark.isActive);

  function setMode(next: ThemeMode): void {
    mode.value = next;
    persistThemeMode(next);
    applyThemeMode(next);
  }

  return { mode, isDark, setMode };
});

if (import.meta.hot) {
  import.meta.hot.accept(acceptHMRUpdate(useThemeStore, import.meta.hot));
}
