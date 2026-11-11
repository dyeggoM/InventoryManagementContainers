import { defineComponent, h, type PropType } from 'vue';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import UsersPanel from './UsersPanel.vue';
import { ACCESS_TOKEN_KEY } from '@services/session';
import type { ManagedUser } from '@services/users';
import { asText } from '@services/utilities';

// The panel's job here is the payload it sends and the one-time password it shows, so the service
// is replaced and the calls are asserted directly.
const mocks = vi.hoisted(() => ({
  list: vi.fn(),
  create: vi.fn(),
  resetPassword: vi.fn(),
  updateRoles: vi.fn(),
  deactivate: vi.fn(),
}));

vi.mock('@services/users', () => ({
  usersService: {
    list: mocks.list,
    create: mocks.create,
    resetPassword: mocks.resetPassword,
    updateRoles: mocks.updateRoles,
    deactivate: mocks.deactivate,
  },
}));

function makeToken(claims: Record<string, unknown>): string {
  const encode = (value: object) =>
    btoa(JSON.stringify(value)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  return `${encode({ alg: 'HS256', typ: 'JWT' })}.${encode(claims)}.signature`;
}

const QInput = defineComponent({
  name: 'QInput',
  props: {
    modelValue: { type: String, default: '' },
    label: { type: String, default: '' },
    type: { type: String, default: 'text' },
    hint: { type: String, default: '' },
  },
  emits: ['update:modelValue'],
  setup(props, { emit }) {
    return () =>
      h('input', {
        'data-label': props.label,
        'data-hint': props.hint,
        type: props.type,
        value: props.modelValue,
        onInput: (event: Event) =>
          emit('update:modelValue', (event.target as HTMLInputElement).value),
      });
  },
});

const QSelect = defineComponent({
  name: 'QSelect',
  props: {
    modelValue: { type: [String, Array] as PropType<string | string[]>, default: '' },
    options: { type: Array as PropType<{ label: string; value: string }[]>, default: () => [] },
  },
  emits: ['update:modelValue'],
  setup(props) {
    return () =>
      h('div', {
        class: 'q-select-stub',
        'data-options': props.options.map((option) => option.value).join(','),
      });
  },
});

const QBtn = defineComponent({
  name: 'QBtn',
  props: {
    label: { type: String, default: '' },
    icon: { type: String, default: '' },
    ariaLabel: { type: String, default: '' },
    disable: { type: Boolean, default: false },
    loading: { type: Boolean, default: false },
    type: { type: String, default: 'button' },
  },
  emits: ['click'],
  setup(props, { emit }) {
    return () =>
      h(
        'button',
        {
          'aria-label': props.ariaLabel,
          'data-label': props.label,
          disabled: props.disable,
          onClick: (event: MouseEvent) => emit('click', event),
        },
        props.label || props.icon,
      );
  },
});

const QForm = defineComponent({
  name: 'QForm',
  emits: ['submit'],
  setup(_props, { slots, emit }) {
    return () =>
      h(
        'form',
        {
          onSubmit: (event: Event) => {
            event.preventDefault();
            emit('submit', event);
          },
        },
        slots.default?.(),
      );
  },
});

const QTableRow = defineComponent({
  name: 'QTableRow',
  props: { row: { type: Object as PropType<Record<string, unknown>>, required: true } },
  setup(_props, { slots }) {
    return () => h('div', { class: 'q-table-row' }, slots.default?.());
  },
});

type Column = { name: string; field: string };

const QTable = defineComponent({
  name: 'QTable',
  props: {
    rows: { type: Array as PropType<Record<string, unknown>[]>, default: () => [] },
    columns: { type: Array as PropType<Column[]>, default: () => [] },
    loading: { type: Boolean, default: false },
  },
  setup(props, { slots }) {
    return () =>
      h(
        'div',
        { class: 'q-table-stub' },
        props.rows.map((row) =>
          h(QTableRow, { row, key: String(row.id) }, () =>
            props.columns.map((column) => {
              const cell = slots[`body-cell-${column.name}`];
              const value = asText(row[column.field]);
              return h(
                'div',
                { class: `cell-${column.name}` },
                cell ? cell({ row, col: column, key: row.id }) : value,
              );
            }),
          ),
        ),
      );
  },
});

const QDialog = defineComponent({
  name: 'QDialog',
  props: { modelValue: { type: Boolean, default: false } },
  setup(props, { slots }) {
    return () => h('div', { class: 'q-dialog-stub' }, props.modelValue ? slots.default?.() : []);
  },
});

const QBadge = defineComponent({
  name: 'QBadge',
  setup(_props, { slots }) {
    return () => h('span', { class: 'q-badge-stub' }, slots.default?.());
  },
});

const QBanner = defineComponent({
  name: 'QBanner',
  setup(_props, { slots }) {
    return () => h('div', { class: 'q-banner-stub' }, slots.default?.());
  },
});

const QIcon = defineComponent({
  name: 'QIcon',
  props: { name: { type: String, default: '' } },
  setup(props) {
    return () => h('i', { class: 'q-icon-stub', 'data-icon': props.name });
  },
});

const QTd = defineComponent({
  name: 'QTd',
  setup(_props, { slots }) {
    return () => h('div', { class: 'q-td-stub' }, slots.default?.());
  },
});

const QCard = defineComponent({
  name: 'QCard',
  setup(_props, { slots }) {
    return () => h('div', { class: 'q-card-stub' }, slots.default?.());
  },
});

const QSpace = defineComponent({ name: 'QSpace', render: () => h('span') });
const QTooltip = defineComponent({ name: 'QTooltip', render: () => h('span') });

const stubs = {
  QInput,
  QSelect,
  QBtn,
  QForm,
  QTable,
  QDialog,
  QBadge,
  QBanner,
  QIcon,
  QSpace,
  QTooltip,
  QTd,
  QCard,
};

function user(overrides: Partial<ManagedUser> = {}): ManagedUser {
  return {
    id: 'u-1',
    email: 'lector@imc.test',
    roles: ['lector'],
    isActive: true,
    lastAccessUtc: null,
    ...overrides,
  };
}

async function mountPanel(rows: ManagedUser[] = [user()]): Promise<VueWrapper> {
  mocks.list.mockResolvedValue(rows);
  localStorage.setItem(
    ACCESS_TOKEN_KEY,
    makeToken({ sub: 'u-9', email: 'root@imc.test', role: ['superadmin'] }),
  );
  const pinia = createPinia();
  setActivePinia(pinia);
  const wrapper = mount(UsersPanel, { global: { plugins: [pinia], stubs } });
  await flushPromises();
  return wrapper;
}

function button(wrapper: VueWrapper, label: string) {
  return wrapper.findAllComponents({ name: 'QBtn' }).find((item) => item.props('label') === label);
}

function ariaButton(wrapper: VueWrapper, prefix: string) {
  return wrapper
    .findAllComponents({ name: 'QBtn' })
    .find((item) => String(item.props('ariaLabel')).startsWith(prefix));
}

function input(wrapper: VueWrapper, label: string) {
  return wrapper.findAll('input').find((item) => item.attributes('data-label') === label)!;
}

beforeEach(() => {
  localStorage.clear();
  mocks.list.mockReset();
  mocks.create.mockReset();
  mocks.resetPassword.mockReset();
  mocks.updateRoles.mockReset();
  mocks.deactivate.mockReset();
});

describe('UsersPanel', () => {
  it('creates a user with no password and shows the generated one in a one-time dialog', async () => {
    const wrapper = await mountPanel();

    mocks.create.mockResolvedValue({ ...user({ id: 'u-2' }), initialPassword: 'Temp-1!' });

    await button(wrapper, 'Nuevo usuario')!.trigger('click');
    await wrapper.vm.$nextTick();
    expect(input(wrapper, 'Contraseña (opcional)').attributes('data-hint')).toContain(
      'Déjala vacía y la API genera una temporal.',
    );

    await input(wrapper, 'Correo').setValue('nuevo@imc.test');
    await wrapper.find('form').trigger('submit');
    await flushPromises();

    // No `password` key at all: an empty field means "let the API generate one".
    expect(mocks.create).toHaveBeenCalledWith({ email: 'nuevo@imc.test', roles: ['lector'] });
    expect(wrapper.text()).toContain('Temp-1!');
    expect(wrapper.text()).toContain('Se muestra solo una vez');
    expect(button(wrapper, 'Copiar')).toBeDefined();
  });

  it('sends the password the caller typed and shows no dialog when the API generated none', async () => {
    const wrapper = await mountPanel();

    mocks.create.mockResolvedValue({ ...user({ id: 'u-2' }), initialPassword: null });

    await button(wrapper, 'Nuevo usuario')!.trigger('click');
    await wrapper.vm.$nextTick();
    await input(wrapper, 'Correo').setValue('jefe@imc.test');
    await input(wrapper, 'Contraseña (opcional)').setValue('Elegida-A-1!');
    await wrapper.find('form').trigger('submit');
    await flushPromises();

    expect(mocks.create).toHaveBeenCalledWith({
      email: 'jefe@imc.test',
      password: 'Elegida-A-1!',
      roles: ['lector'],
    });
    expect(wrapper.text()).not.toContain('Se muestra solo una vez');
  });

  it('resets a password to a generated one and shows it once', async () => {
    const wrapper = await mountPanel();
    mocks.resetPassword.mockResolvedValue({ initialPassword: 'Otra-2!' });

    await ariaButton(wrapper, 'Restablecer contraseña de')!.trigger('click');
    await wrapper.vm.$nextTick();
    expect(wrapper.text()).toContain('Al restablecerla se cierran todas las sesiones activas');

    await wrapper.find('form').trigger('submit');
    await flushPromises();

    expect(mocks.resetPassword).toHaveBeenCalledWith('u-1', undefined);
    expect(wrapper.text()).toContain('Otra-2!');
    expect(wrapper.text()).toContain('Se muestra solo una vez');
  });

  it('resets a password to the one the caller chose, without a one-time dialog', async () => {
    const wrapper = await mountPanel();
    mocks.resetPassword.mockResolvedValue({ initialPassword: null });

    await ariaButton(wrapper, 'Restablecer contraseña de')!.trigger('click');
    await wrapper.vm.$nextTick();
    await input(wrapper, 'Contraseña (opcional)').setValue('Nueva-2026!');
    await wrapper.find('form').trigger('submit');
    await flushPromises();

    expect(mocks.resetPassword).toHaveBeenCalledWith('u-1', 'Nueva-2026!');
    expect(wrapper.text()).not.toContain('Se muestra solo una vez');
  });
});
