import { defineComponent, h, type PropType } from 'vue';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import AuditPanel from './AuditPanel.vue';
import type { AuditEntry } from '@services/audit';
import { asText } from '@services/utilities';

// The panel's job is the query it sends and the reader it opens, so the service is replaced and the
// calls are asserted directly. `formatAuditPayload` stays real: the dialog shows what it produces.
const mocks = vi.hoisted(() => ({ search: vi.fn() }));

vi.mock('@services/audit', async (importOriginal) => {
  const actual = await importOriginal<Record<string, unknown>>();
  return { ...actual, auditService: { search: mocks.search } };
});

const QInput = defineComponent({
  name: 'QInput',
  props: {
    modelValue: { type: String, default: '' },
    label: { type: String, default: '' },
    type: { type: String, default: 'text' },
  },
  emits: ['update:modelValue'],
  setup(props, { emit }) {
    return () =>
      h('input', {
        'data-label': props.label,
        type: props.type,
        value: props.modelValue,
        onInput: (event: Event) =>
          emit('update:modelValue', (event.target as HTMLInputElement).value),
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

type Column = { name: string; field: string };

/** One row of the stub table, as a component so the test can address its cells. */
const QTableRow = defineComponent({
  name: 'QTableRow',
  props: { row: { type: Object as PropType<Record<string, unknown>>, required: true } },
  setup(_props, { slots }) {
    return () => h('div', { class: 'q-table-row' }, slots.default?.());
  },
});

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

const passthrough = (name: string) =>
  defineComponent({
    name,
    setup(_props, { slots }) {
      return () => h('div', { class: `${name}-stub` }, slots.default?.());
    },
  });

const QBadge = passthrough('QBadge');
const QBanner = passthrough('QBanner');
const QCard = passthrough('QCard');
const QCardSection = passthrough('QCardSection');
const QSeparator = passthrough('QSeparator');
const QTd = passthrough('QTd');

const QIcon = defineComponent({
  name: 'QIcon',
  props: { name: { type: String, default: '' } },
  setup(props) {
    return () => h('i', { class: 'q-icon-stub', 'data-icon': props.name });
  },
});

const QSpace = defineComponent({ name: 'QSpace', render: () => h('span') });

const stubs = {
  QInput,
  QBtn,
  QTable,
  QDialog,
  QBadge,
  QBanner,
  QCard,
  QCardSection,
  QSeparator,
  QTd,
  QIcon,
  QSpace,
};

function entry(overrides: Partial<AuditEntry> = {}): AuditEntry {
  return {
    id: 1,
    occurredAtUtc: '2026-11-10T12:30:00Z',
    actorEmail: 'root@imc.test',
    action: 'Update',
    entity: 'Item',
    entityId: 7,
    before: { label: 'Taladro' },
    after: { label: 'Taladro grande' },
    ...overrides,
  };
}

function pageOf(items: AuditEntry[], total: number | null = items.length) {
  return { items, page: 1, pageSize: 25, total };
}

async function mountPanel(mobile = false): Promise<VueWrapper> {
  const wrapper = mount(AuditPanel, {
    global: {
      stubs,
      // `$q` is a Quasar plugin global property, injected the way every spec provides one.
      mocks: { $q: { screen: { lt: { sm: mobile } } } },
    },
  });
  await flushPromises();
  return wrapper;
}

function nextPageButton(wrapper: VueWrapper) {
  return wrapper.find('button[aria-label="Página siguiente"]');
}

beforeEach(() => {
  mocks.search.mockReset();
});

describe('AuditPanel', () => {
  it('asks for the first page with the empty filters, as the panel always has', async () => {
    mocks.search.mockResolvedValue(pageOf([entry()]));

    await mountPanel();

    expect(mocks.search).toHaveBeenCalledWith({
      actor: '',
      entity: '',
      action: '',
      from: '',
      to: '',
      page: 1,
      pageSize: 25,
    });
  });

  it('renders each change as a card below sm, and opens before/after from the card', async () => {
    mocks.search.mockResolvedValue(pageOf([entry()]));

    const wrapper = await mountPanel(true);

    // Five columns do not fit a phone: the row becomes a card and the table is not rendered.
    expect(wrapper.find('.q-table-stub').exists()).toBe(false);

    const card = wrapper.find('.audit-card');
    expect(card.exists()).toBe(true);
    expect(card.text()).toContain('Update · Item');
    expect(card.text()).toContain('#7');
    expect(card.text()).toContain('root@imc.test');
    expect(card.text()).toContain(new Date('2026-11-10T12:30:00Z').toLocaleString());

    // The card itself is the tap target of the before/after viewer.
    await card.trigger('click');
    await flushPromises();

    expect(wrapper.text()).toContain('Antes');
    expect(wrapper.text()).toContain('Después');
    expect(wrapper.text()).toContain('Taladro grande');
  });

  it('keeps the table and hides the cards at sm and above', async () => {
    mocks.search.mockResolvedValue(pageOf([entry()]));

    const wrapper = await mountPanel(false);

    expect(wrapper.find('.q-table-stub').exists()).toBe(true);
    expect(wrapper.findAll('.q-table-row')).toHaveLength(1);
    expect(wrapper.find('.audit-card').exists()).toBe(false);
  });

  it('stacks the filters and their buttons on a phone, and not above sm', async () => {
    mocks.search.mockResolvedValue(pageOf([]));

    const mobile = await mountPanel(true);
    expect(mobile.find('.filters').classes()).toContain('column');
    for (const field of mobile.findAll('.filters > div')) {
      expect(field.classes()).toContain('full-width');
    }
    expect(mobile.find('.filter-actions').classes()).toContain('full-width');

    const desktop = await mountPanel(false);
    expect(desktop.find('.filters').classes()).toContain('row');
    expect(desktop.findAll('.filters > div')[0]!.classes()).toContain('col-md-2');
  });

  it('keeps the first page total when a later page answers without one', async () => {
    const rows = Array.from({ length: 25 }, (_value, index) => entry({ id: index + 1 }));
    mocks.search
      .mockResolvedValueOnce(pageOf(rows, 60))
      .mockResolvedValueOnce({ items: rows, page: 2, pageSize: 25, total: null });

    const wrapper = await mountPanel(true);
    expect(wrapper.text()).toContain('60 registro(s) · página 1');
    expect(nextPageButton(wrapper).attributes('disabled')).toBeUndefined();

    await nextPageButton(wrapper).trigger('click');
    await flushPromises();

    // A null total must not erase the count the first page reported.
    expect(wrapper.text()).toContain('60 registro(s) · página 2');
  });
});
