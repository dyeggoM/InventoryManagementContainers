/**
 * Node 26 ships a `localStorage` global that only works when the process is started with
 * `--localstorage-file`, and it shadows the one jsdom would provide — so any module touching it
 * (the auth store restores the session from it) throws before a test runs. Give the app a plain
 * in-memory store instead.
 */
class MemoryStorage implements Storage {
  private readonly data = new Map<string, string>();

  get length(): number {
    return this.data.size;
  }

  clear(): void {
    this.data.clear();
  }

  getItem(key: string): string | null {
    return this.data.get(key) ?? null;
  }

  key(index: number): string | null {
    return [...this.data.keys()][index] ?? null;
  }

  removeItem(key: string): void {
    this.data.delete(key);
  }

  setItem(key: string, value: string): void {
    this.data.set(key, String(value));
  }
}

Object.defineProperty(globalThis, 'localStorage', {
  value: new MemoryStorage(),
  configurable: true,
  writable: true,
});

/**
 * jsdom ships no `matchMedia`, and Quasar's Dark plugin asks for it as soon as the mode is `auto` —
 * which is the app's default. Answer "light" so `auto` resolves the same way on every run instead
 * of depending on the machine the suite happens to run on.
 */
if (typeof window.matchMedia !== 'function') {
  const lightPreference = {
    matches: false,
    media: '(prefers-color-scheme: dark)',
    onchange: null,
    addListener: () => undefined,
    removeListener: () => undefined,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
    dispatchEvent: () => false,
  };

  Object.defineProperty(window, 'matchMedia', {
    value: (query: string) => ({ ...lightPreference, media: query }),
    configurable: true,
    writable: true,
  });
}
