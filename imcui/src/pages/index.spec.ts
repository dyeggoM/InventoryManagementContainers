import { defineComponent, h } from 'vue';
import { beforeEach, describe, expect, it } from 'vitest';
import { mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { createMemoryHistory, createRouter } from 'vue-router';
import { Dark } from 'quasar';
import IndexPage from './index.vue';
import { useThemeStore } from '../stores/theme-store';

/**
 * The header control is the only way for the operator to choose a mode, so this mounts the real
 * layout and clicks it. Quasar's components are stubbed down to the parts the assertion reads (the
 * button's icon, the items' labels and their tick); the store and Quasar's Dark plugin are real, so
 * `imc_theme` and `body--dark` are the app's own.
 */

/** Slots only: the layout and the drawers add nothing this spec reads. */
function passthrough(name: string) {
  return defineComponent({
    name,
    setup(_props, { slots }) {
      return () => h('div', { class: `${name}-stub` }, slots.default?.());
    },
  });
}

const QBtn = defineComponent({
  name: 'QBtn',
  props: {
    icon: { type: String, default: '' },
    label: { type: String, default: '' },
  },
  setup(props, { slots }) {
    return () => h('button', { 'data-icon': props.icon }, slots.default?.());
  },
});

const QItem = defineComponent({
  name: 'QItem',
  props: {
    clickable: { type: Boolean, default: false },
    active: { type: Boolean, default: false },
  },
  setup(props, { slots }) {
    return () =>
      h('div', { class: 'q-item-stub', 'data-active': String(props.active) }, slots.default?.());
  },
});

const QItemSection = defineComponent({
  name: 'QItemSection',
  setup(_props, { slots }) {
    return () => h('div', { class: 'q-item-section-stub' }, slots.default?.());
  },
});

const QItemLabel = defineComponent({
  name: 'QItemLabel',
  setup(_props, { slots }) {
    return () => h('div', { class: 'q-item-label-stub' }, slots.default?.());
  },
});

const QIcon = defineComponent({
  name: 'QIcon',
  props: { name: { type: String, default: '' } },
  setup(props) {
    return () => h('i', { class: 'q-icon-stub', 'data-icon': props.name });
  },
});

const stubs = {
  QLayout: passthrough('QLayout'),
  QHeader: passthrough('QHeader'),
  QToolbar: passthrough('QToolbar'),
  QToolbarTitle: passthrough('QToolbarTitle'),
  QDrawer: passthrough('QDrawer'),
  QPageContainer: passthrough('QPageContainer'),
  QList: passthrough('QList'),
  QMenu: passthrough('QMenu'),
  QSeparator: passthrough('QSeparator'),
  QBtn,
  QItem,
  QItemSection,
  QItemLabel,
  QIcon,
  RouterView: passthrough('RouterView'),
  'router-view': passthrough('RouterView'),
};

const directives = { closePopup: {}, 'close-popup': {}, ripple: {} };

async function mountPage(): Promise<VueWrapper> {
  const pinia = createPinia();
  setActivePinia(pinia);
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/:pathMatch(.*)*', component: { render: () => null } }],
  });
  await router.push('/inventory');
  await router.isReady();
  return mount(IndexPage, { global: { plugins: [pinia, router], stubs, directives } });
}

/** The theme button is the one whose accessible name carries the current mode. */
function themeButton(wrapper: VueWrapper) {
  return wrapper.find('button[aria-label^="Tema:"]');
}

function themeOption(wrapper: VueWrapper, label: string) {
  const item = wrapper
    .findAllComponents({ name: 'QItem' })
    .find((candidate) => candidate.text().trim() === label);
  if (!item) throw new Error(`no theme option labelled ${label}`);
  return item;
}

describe('header theme control', () => {
  beforeEach(() => {
    localStorage.clear();
    Dark.set(false);
  });

  it('offers the three modes, with the system one active by default', async () => {
    const wrapper = await mountPage();

    expect(themeButton(wrapper).attributes('aria-label')).toBe('Tema: Sistema');
    expect(themeButton(wrapper).attributes('data-icon')).toBe('brightness_auto');

    for (const label of ['Sistema', 'Claro', 'Oscuro']) {
      expect(themeOption(wrapper, label).exists()).toBe(true);
    }

    // The tick is on the mode in force, and only there.
    expect(themeOption(wrapper, 'Sistema').find('[data-icon="check"]').exists()).toBe(true);
    expect(themeOption(wrapper, 'Oscuro').find('[data-icon="check"]').exists()).toBe(false);
  });

  it('switches to dark when Oscuro is picked, and remembers it', async () => {
    const wrapper = await mountPage();

    await themeOption(wrapper, 'Oscuro').trigger('click');

    expect(useThemeStore().mode).toBe('dark');
    expect(localStorage.getItem('imc_theme')).toBe('dark');
    expect(Dark.isActive).toBe(true);
    expect(document.body.classList.contains('body--dark')).toBe(true);
    // The button now says "dark" without a reload.
    expect(themeButton(wrapper).attributes('aria-label')).toBe('Tema: Oscuro');
    expect(themeButton(wrapper).attributes('data-icon')).toBe('dark_mode');
    expect(themeOption(wrapper, 'Oscuro').find('[data-icon="check"]').exists()).toBe(true);
    expect(themeOption(wrapper, 'Sistema').find('[data-icon="check"]').exists()).toBe(false);
  });

  it('shows the stored choice instead of the system one', async () => {
    localStorage.setItem('imc_theme', 'light');

    const wrapper = await mountPage();

    expect(themeButton(wrapper).attributes('aria-label')).toBe('Tema: Claro');
    expect(themeButton(wrapper).attributes('data-icon')).toBe('light_mode');
    expect(themeOption(wrapper, 'Claro').find('[data-icon="check"]').exists()).toBe(true);
    expect(Dark.isActive).toBe(false);
  });
});
