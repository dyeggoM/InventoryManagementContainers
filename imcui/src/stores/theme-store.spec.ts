import { beforeEach, describe, expect, it, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { Dark } from 'quasar';
import { useThemeStore } from './theme-store';

/**
 * The store's whole job is to decide *which* mode Quasar applies, so every test asserts both halves:
 * the choice it holds and the call that reaches Quasar's Dark plugin. The suite resolves Quasar's
 * client build (vitest.config.ts) and answers `matchMedia` with "light" (test-setup.ts), which is
 * why `body--dark`/`body--light` and `Dark.isActive` are assertable here.
 */
const THEME_KEY = 'imc_theme';

describe('theme store', () => {
  beforeEach(() => {
    localStorage.clear();
    setActivePinia(createPinia());
    // Every test starts from a known mode: the previous one may have left the body dark.
    Dark.set(false);
  });

  it('starts on auto and hands the decision to Quasar', () => {
    const set = vi.spyOn(Dark, 'set');

    const store = useThemeStore();

    expect(store.mode).toBe('auto');
    expect(set).toHaveBeenCalledWith('auto');
    // Nothing was stored: nobody has chosen anything yet, so the OS keeps deciding.
    expect(localStorage.getItem(THEME_KEY)).toBeNull();

    set.mockRestore();
  });

  it('restores a stored dark choice and applies it on creation', () => {
    localStorage.setItem(THEME_KEY, 'dark');
    const set = vi.spyOn(Dark, 'set');

    const store = useThemeStore();

    expect(store.mode).toBe('dark');
    expect(set).toHaveBeenCalledWith(true);
    expect(store.isDark).toBe(true);
    expect(Dark.isActive).toBe(true);
    expect(document.body.classList.contains('body--dark')).toBe(true);

    set.mockRestore();
  });

  it('persists and applies a new mode', () => {
    const store = useThemeStore();
    const set = vi.spyOn(Dark, 'set');

    store.setMode('light');

    expect(store.mode).toBe('light');
    expect(localStorage.getItem(THEME_KEY)).toBe('light');
    expect(set).toHaveBeenCalledWith(false);
    expect(Dark.isActive).toBe(false);
    expect(document.body.classList.contains('body--light')).toBe(true);

    set.mockRestore();
  });

  it('falls back to auto when the stored value is not one of the three modes', () => {
    localStorage.setItem(THEME_KEY, 'purple');
    const set = vi.spyOn(Dark, 'set');

    const store = useThemeStore();

    expect(store.mode).toBe('auto');
    expect(set).toHaveBeenCalledWith('auto');

    set.mockRestore();
  });

  it('reports isDark from what Quasar applied, and keeps following the system on auto', () => {
    const store = useThemeStore();

    store.setMode('dark');
    expect(store.isDark).toBe(true);

    store.setMode('light');
    expect(store.isDark).toBe(false);

    store.setMode('auto');
    // Not forced any more: Quasar holds "auto" and its own answer is what the store reports.
    expect(Dark.mode).toBe('auto');
    expect(store.isDark).toBe(Dark.isActive);
  });
});
