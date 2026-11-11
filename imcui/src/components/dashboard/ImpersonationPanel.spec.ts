import { defineComponent, h, type PropType } from 'vue';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import ImpersonationPanel from './ImpersonationPanel.vue';
import { ACCESS_TOKEN_KEY } from '@services/session';
import { setLoginRedirect } from '@services/api';

/** An unsigned token with the claims the API signs, so the store reads a real role out of it. */
function makeToken(claims: Record<string, unknown>): string {
  const encode = (value: object) =>
    btoa(JSON.stringify(value)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  return `${encode({ alg: 'HS256', typ: 'JWT' })}.${encode(claims)}.signature`;
}

function signedInAs(role: string): void {
  localStorage.setItem(
    ACCESS_TOKEN_KEY,
    makeToken({
      sub: 'u-1',
      email: 'root@imc.test',
      unique_name: 'root',
      role: [role],
      exp: Math.floor(Date.now() / 1000) + 3600,
    }),
  );
}

function borrowedToken(role: string): string {
  return makeToken({
    sub: 'u-1',
    email: 'root@imc.test',
    unique_name: 'root',
    role: [role],
    act: 'u-1',
    act_email: 'root@imc.test',
    scope: 'read',
    exp: Math.floor(Date.now() / 1000) + 420,
  });
}

function postedBody(calls: { init: RequestInit | undefined }[]): unknown {
  const body = calls[0]?.init?.body;
  return typeof body === 'string' ? JSON.parse(body) : undefined;
}

function jsonResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    statusText: String(status),
    json: () => Promise.resolve(body),
  } as unknown as Response;
}

const QBtn = defineComponent({
  name: 'QBtn',
  props: {
    label: { type: String, default: '' },
    icon: { type: String, default: '' },
    disable: { type: Boolean, default: false },
    loading: { type: Boolean, default: false },
  },
  emits: ['click'],
  setup(props, { emit }) {
    return () =>
      h(
        'button',
        {
          disabled: props.disable,
          'data-label': props.label,
          onClick: (event: MouseEvent) => emit('click', event),
        },
        props.label || props.icon,
      );
  },
});

const QSelect = defineComponent({
  name: 'QSelect',
  props: {
    modelValue: { type: String as PropType<string | null>, default: null },
    options: { type: Array as PropType<{ label: string; value: string }[]>, default: () => [] },
    label: { type: String, default: '' },
    disable: { type: Boolean, default: false },
    hint: { type: String, default: '' },
  },
  emits: ['update:modelValue'],
  setup(props) {
    return () =>
      h('div', {
        class: 'q-select-stub',
        'data-label': props.label,
        'data-options': props.options.map((option) => option.value).join(','),
        'data-hint': props.hint,
      });
  },
});

const QBanner = defineComponent({
  name: 'QBanner',
  setup(_props, { slots }) {
    return () => h('div', { class: 'q-banner-stub' }, slots.default?.());
  },
});

const QCard = defineComponent({
  name: 'QCard',
  setup(_props, { slots }) {
    return () => h('div', { class: 'q-card-stub' }, slots.default?.());
  },
});

const QIcon = defineComponent({
  name: 'QIcon',
  props: { name: { type: String, default: '' } },
  setup(props) {
    return () => h('i', { class: 'q-icon-stub', 'data-icon': props.name });
  },
});

const QSpace = defineComponent({ name: 'QSpace', render: () => h('span') });

const stubs = { QBtn, QSelect, QBanner, QCard, QIcon, QSpace };

function mountPanel(): VueWrapper {
  const pinia = createPinia();
  setActivePinia(pinia);
  return mount(ImpersonationPanel, { global: { plugins: [pinia], stubs } });
}

function button(wrapper: VueWrapper, label: string) {
  return wrapper.findAllComponents({ name: 'QBtn' }).find((item) => item.props('label') === label);
}

function picker(wrapper: VueWrapper) {
  return wrapper.findComponent({ name: 'QSelect' });
}

async function chooseRole(wrapper: VueWrapper, role: string): Promise<void> {
  picker(wrapper).vm.$emit('update:modelValue', role);
  await wrapper.vm.$nextTick();
}

beforeEach(() => {
  localStorage.clear();
  vi.unstubAllGlobals();
  setLoginRedirect(() => undefined);
});

describe('ImpersonationPanel', () => {
  it('offers a superadmin the whole hierarchy in one caption line', () => {
    signedInAs('superadmin');

    const wrapper = mountPanel();

    expect(wrapper.text()).toContain('Puedes entrar como admin, operador o lector.');
    expect(picker(wrapper).attributes('data-options')).toBe('admin,operador,lector');
    // The picker never carries the caller's own role, let alone superadmin.
    expect(picker(wrapper).attributes('data-options')).not.toContain('superadmin');
  });

  it('offers an admin only what sits below an admin', () => {
    signedInAs('admin');

    const wrapper = mountPanel();

    expect(wrapper.text()).toContain('Puedes entrar como operador o lector.');
    expect(picker(wrapper).attributes('data-options')).toBe('operador,lector');
  });

  it('borrows the chosen role and shows the banner instead of the picker', async () => {
    signedInAs('superadmin');
    const calls: { url: string; init: RequestInit | undefined }[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn((url: string, init?: RequestInit) => {
        calls.push({ url, init });
        return Promise.resolve(
          jsonResponse({
            access_token: borrowedToken('operador'),
            role: 'operador',
            scope: 'read',
            impersonated_by: { id: 'u-1', email: 'root@imc.test' },
          }),
        );
      }),
    );

    const wrapper = mountPanel();
    await chooseRole(wrapper, 'operador');
    await button(wrapper, 'Suplantar')!.trigger('click');
    await flushPromises();

    expect(calls[0]?.url).toContain('/users/impersonate');
    expect(postedBody(calls)).toEqual({ role: 'operador' });
    expect(wrapper.text()).toContain('Suplantando: rol operador');
    expect(wrapper.text()).toContain('toda acción de escritura queda deshabilitada');
    expect(button(wrapper, 'Salir')).toBeDefined();
    // Whoever already holds an impersonation token is not offered the picker again.
    expect(picker(wrapper).exists()).toBe(false);
  });

  it('never shows the picker to a lector', () => {
    signedInAs('lector');

    const wrapper = mountPanel();

    expect(picker(wrapper).exists()).toBe(false);
    expect(button(wrapper, 'Suplantar')).toBeUndefined();
    expect(wrapper.text()).toContain('Tu rol no permite suplantar a nadie');
  });

  it("renders the server's own message when the API refuses the role", async () => {
    signedInAs('superadmin');
    vi.stubGlobal(
      'fetch',
      vi.fn(() =>
        Promise.resolve(jsonResponse({ error: 'Target role must be below caller role.' }, 400)),
      ),
    );

    const wrapper = mountPanel();
    await chooseRole(wrapper, 'operador');
    await button(wrapper, 'Suplantar')!.trigger('click');
    await flushPromises();

    expect(wrapper.text()).toContain('Target role must be below caller role.');
    // The picker stays: nothing was borrowed.
    expect(picker(wrapper).exists()).toBe(true);
  });

  it('restores the panel to the picker when the impersonation is left', async () => {
    signedInAs('superadmin');
    const calls: { url: string; init: RequestInit | undefined }[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn((url: string, init?: RequestInit) => {
        calls.push({ url, init });
        // Leaving is a round-trip: the API answers the caller's own token and writes the audit row.
        return Promise.resolve(
          url.includes('/auth/stop-impersonation')
            ? jsonResponse({
                access_token: makeToken({
                  sub: 'u-1',
                  email: 'root@imc.test',
                  unique_name: 'root',
                  role: ['superadmin'],
                  exp: Math.floor(Date.now() / 1000) + 3600,
                }),
                expires_at_utc: null,
              })
            : jsonResponse({
                access_token: borrowedToken('lector'),
                role: 'lector',
                scope: 'read',
                impersonated_by: { id: 'u-1', email: 'root@imc.test' },
              }),
        );
      }),
    );

    const wrapper = mountPanel();
    await chooseRole(wrapper, 'lector');
    await button(wrapper, 'Suplantar')!.trigger('click');
    await flushPromises();
    expect(picker(wrapper).exists()).toBe(false);

    await button(wrapper, 'Salir')!.trigger('click');
    await flushPromises();

    expect(calls.some((call) => call.url.includes('/auth/stop-impersonation'))).toBe(true);
    expect(wrapper.text()).not.toContain('Suplantando:');
    expect(picker(wrapper).exists()).toBe(true);
  });
});
