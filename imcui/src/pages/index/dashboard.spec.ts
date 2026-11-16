import { defineComponent, h } from 'vue';
import { beforeEach, describe, expect, it } from 'vitest';
import { mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import DashboardPage from './dashboard.vue';
import { ACCESS_TOKEN_KEY } from '@services/session';

/**
 * The one decision this page takes per screen size is the tab bar's wording: the five tabs do not
 * fit a 390px phone while the import tab says "Importar desde Excel" (measured: 534px of tabs in a
 * 356px bar, which left the last tab entirely off screen), so below `sm` that one label is
 * shortened and the full name moves to the accessible label. Everything else on the page is the
 * panels' own business and has its own spec; here they are stubs.
 */

function makeToken(claims: Record<string, unknown>): string {
  const encode = (value: object) =>
    btoa(JSON.stringify(value)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  return `${encode({ alg: 'HS256', typ: 'JWT' })}.${encode(claims)}.signature`;
}

const QTab = defineComponent({
  name: 'QTab',
  props: {
    name: { type: String, default: '' },
    label: { type: String, default: '' },
    icon: { type: String, default: '' },
    ariaLabel: { type: String, default: '' },
  },
  setup(props) {
    return () =>
      h(
        'button',
        {
          class: 'q-tab-stub',
          'data-name': props.name,
          'aria-label': props.ariaLabel || undefined,
        },
        props.label,
      );
  },
});

const passthrough = (name: string) =>
  defineComponent({
    name,
    setup(_props, { slots }) {
      return () => h('div', { class: `${name}-stub` }, slots.default?.());
    },
  });

const QTabs = defineComponent({
  name: 'QTabs',
  props: { modelValue: { type: String, default: '' } },
  emits: ['update:modelValue'],
  setup(_props, { slots }) {
    return () => h('div', { class: 'q-tabs-stub' }, slots.default?.());
  },
});

const QTabPanels = defineComponent({
  name: 'QTabPanels',
  props: { modelValue: { type: String, default: '' } },
  emits: ['update:modelValue'],
  setup(_props, { slots }) {
    return () => h('div', { class: 'q-tab-panels-stub' }, slots.default?.());
  },
});

const stubs = {
  QPage: passthrough('QPage'),
  QCard: passthrough('QCard'),
  QBanner: passthrough('QBanner'),
  QSeparator: passthrough('QSeparator'),
  QTabPanel: passthrough('QTabPanel'),
  QTabPanels,
  QTabs,
  QTab,
  QIcon: defineComponent({
    name: 'QIcon',
    props: { name: { type: String, default: '' } },
    setup(props) {
      return () => h('i', { class: 'q-icon-stub', 'data-icon': props.name });
    },
  }),
  UsersPanel: passthrough('UsersPanel'),
  AuditPanel: passthrough('AuditPanel'),
  ArchivePanel: passthrough('ArchivePanel'),
  ImportPanel: passthrough('ImportPanel'),
  ImpersonationPanel: passthrough('ImpersonationPanel'),
};

function mountPage(mobile = false, roles: string[] = ['superadmin']): VueWrapper {
  localStorage.setItem(
    ACCESS_TOKEN_KEY,
    makeToken({ sub: 'u-9', email: 'root@imc.test', role: roles }),
  );
  const pinia = createPinia();
  setActivePinia(pinia);
  return mount(DashboardPage, {
    global: {
      plugins: [pinia],
      stubs,
      // `$q` is Quasar's screen plugin, injected here the way every spec injects it.
      mocks: { $q: { screen: { lt: { sm: mobile } } } },
    },
  });
}

function tabs(wrapper: VueWrapper) {
  return wrapper.findAllComponents({ name: 'QTab' });
}

function labels(wrapper: VueWrapper): string[] {
  return tabs(wrapper).map((tab) => String(tab.props('label')));
}

beforeEach(() => {
  localStorage.clear();
});

describe('DashboardPage', () => {
  it('spells the import tab out in full at sm and above', () => {
    const wrapper = mountPage(false);

    expect(labels(wrapper)).toEqual([
      'Usuarios',
      'Auditoría',
      'Archivo',
      'Importar desde Excel',
      'Suplantación',
    ]);
  });

  it('shortens it on a phone, and keeps the full name as the accessible label', () => {
    const wrapper = mountPage(true);

    // The five labels have to share 356px: "Importar desde Excel" is 165px of that on its own.
    expect(labels(wrapper)).toEqual([
      'Usuarios',
      'Auditoría',
      'Archivo',
      'Importar',
      'Suplantación',
    ]);

    // Shortened on screen, never for a screen reader: the same name both sizes.
    const importTab = tabs(wrapper).find((tab) => tab.props('name') === 'import');
    expect(importTab?.attributes('aria-label')).toBe('Importar desde Excel');
  });

  it('keeps the five panels reachable whatever the label says', () => {
    const wrapper = mountPage(true);

    // One tab per panel and none missing: the fix must not trade a clipped tab for a lost one.
    expect(tabs(wrapper).map((tab) => tab.props('name'))).toEqual([
      'users',
      'audit',
      'archive',
      'import',
      'impersonation',
    ]);
  });

  it('still shows an admin only the two panels their role can open', () => {
    const wrapper = mountPage(true, ['admin']);

    expect(tabs(wrapper).map((tab) => tab.props('name'))).toEqual(['import', 'impersonation']);
  });
});
