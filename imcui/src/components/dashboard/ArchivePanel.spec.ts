import { defineComponent, h, type PropType } from 'vue';
import { describe, expect, it, beforeEach, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import ArchivePanel from './ArchivePanel.vue';
import { ApiError } from '@services/api';
import type { ArchiveEntry } from '@services/archive';
import { asText } from '@services/utilities';

// The panel talks to the API and to Quasar's notifications; both are replaced so the test sees the
// filtered call shape and the copy the user reads, not a network or a toast queue.
const mocks = vi.hoisted(() => ({
  list: vi.fn(),
  restore: vi.fn(),
  success: vi.fn(),
  error: vi.fn(),
  info: vi.fn(),
}));

vi.mock('@services/archive', () => ({
  archiveService: { list: mocks.list, restore: mocks.restore },
}));

vi.mock('../../composables/useFeedback', () => ({
  useFeedback: () => ({
    success: mocks.success,
    error: mocks.error,
    info: mocks.info,
    confirmDelete: vi.fn(),
    confirmDiscard: vi.fn(),
  }),
}));

const QInput = defineComponent({
  name: 'QInput',
  props: {
    modelValue: { type: String, default: '' },
    label: { type: String, default: '' },
    type: { type: String, default: 'text' },
  },
  emits: ['update:modelValue'],
  render() {
    return h('input', { value: this.modelValue, type: this.type, 'data-label': this.label });
  },
});

const QBtn = defineComponent({
  name: 'QBtn',
  props: {
    label: { type: String, default: '' },
    icon: { type: String, default: '' },
    disable: { type: Boolean, default: false },
    loading: { type: Boolean, default: false },
  },
  emits: ['click'],
  render() {
    return h(
      'button',
      {
        disabled: this.disable,
        onClick: (event: MouseEvent) => this.$emit('click', event),
      },
      this.label || this.icon,
    );
  },
});

type Column = { name: string; field: string };

/** One row of the stub table, as a component so the test can address its buttons by type. */
const QTableRow = defineComponent({
  name: 'QTableRow',
  props: { row: { type: Object as PropType<Record<string, unknown>>, required: true } },
  setup(_props, { slots }) {
    return () => h('div', { class: 'q-table-row' }, slots.default?.());
  },
});

/** A table that keeps the rows and the scoped slots visible: the panel's cells are what is tested. */
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
          h(QTableRow, { row, key: String(row.archiveId) }, () =>
            props.columns.map((column) => {
              const cell = slots[`body-cell-${column.name}`];
              const value = asText(row[column.field]);
              return h(
                'div',
                { class: `cell-${column.name}` },
                cell ? cell({ row, col: column, key: row.archiveId }) : value,
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
  QBtn,
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

function entry(overrides: Partial<ArchiveEntry> = {}): ArchiveEntry {
  return {
    archiveId: 11,
    itemId: 7,
    label: 'Taladro',
    icon: 'construction',
    parentId: null,
    attributes: '{"Brand":"DeWalt"}',
    isActive: false,
    createdAt: '2026-11-01T09:00:00Z',
    createdBy: 'root@imc.test',
    updatedAt: null,
    updatedBy: null,
    deletedAtUtc: '2026-11-10T12:30:00Z',
    deletedById: 'user-1',
    deletedBy: 'root@imc.test',
    attachmentsJson: null,
    restoredAtUtc: null,
    restoredById: null,
    ...overrides,
  };
}

function page(items: ArchiveEntry[], total = items.length) {
  return { items, page: 1, pageSize: 25, total };
}

async function mountPanel(): Promise<VueWrapper> {
  const wrapper = mount(ArchivePanel, { global: { stubs } });
  await flushPromises();
  return wrapper;
}

function rowAt(wrapper: VueWrapper, index: number) {
  return wrapper.findAllComponents({ name: 'QTableRow' })[index]!;
}

function restoreButton(row: VueWrapper) {
  return row
    .findAllComponents({ name: 'QBtn' })
    .find((button) => button.props('label') === 'Restaurar')!;
}

beforeEach(() => {
  localStorage.clear();
  mocks.list.mockReset();
  mocks.restore.mockReset();
  mocks.success.mockReset();
  mocks.error.mockReset();
  mocks.info.mockReset();
});

describe('ArchivePanel', () => {
  it('lists the archive with the default page and no filters', async () => {
    mocks.list.mockResolvedValue(page([entry()]));

    await mountPanel();

    expect(mocks.list).toHaveBeenCalledTimes(1);
    expect(mocks.list).toHaveBeenCalledWith({
      label: '',
      actor: '',
      from: '',
      to: '',
      page: 1,
      pageSize: 25,
    });
  });

  it('shows the label, the localised deletion date, the actor and the state', async () => {
    mocks.list.mockResolvedValue(
      page([entry(), entry({ archiveId: 12, itemId: 8, restoredAtUtc: '2026-11-11T08:00:00Z' })]),
    );

    const wrapper = await mountPanel();
    const [archived, restored] = [rowAt(wrapper, 0), rowAt(wrapper, 1)];

    expect(archived.text()).toContain('Taladro');
    expect(archived.text()).toContain(new Date('2026-11-10T12:30:00Z').toLocaleString());
    expect(archived.text()).toContain('root@imc.test');
    expect(archived.find('.cell-state').text()).toBe('En archivo');
    expect(restored.find('.cell-state').text()).toBe('Restaurado');
  });

  it('marks a row that kept its attachments and leaves a plain one unmarked', async () => {
    mocks.list.mockResolvedValue(
      page([entry({ attachmentsJson: '[{"id":1}]' }), entry({ archiveId: 12 })]),
    );

    const wrapper = await mountPanel();

    expect(rowAt(wrapper, 0).find('.cell-attachments').find('.q-icon-stub').exists()).toBe(true);
    expect(rowAt(wrapper, 1).find('.cell-attachments').find('.q-icon-stub').exists()).toBe(false);
  });

  it('disables Restaurar on a row that is already restored', async () => {
    mocks.list.mockResolvedValue(page([entry({ restoredAtUtc: '2026-11-11T08:00:00Z' })]));

    const wrapper = await mountPanel();

    expect(restoreButton(rowAt(wrapper, 0)).props('disable')).toBe(true);
  });

  it('restores only after the confirmation, then reloads the list', async () => {
    mocks.list.mockResolvedValue(page([entry()]));
    mocks.restore.mockResolvedValue({ id: 7, label: 'Taladro' });

    const wrapper = await mountPanel();
    await restoreButton(rowAt(wrapper, 0)).trigger('click');

    // Nothing happened yet: the first click only opens the dialog.
    expect(mocks.restore).not.toHaveBeenCalled();
    expect(wrapper.text()).toContain('Restaurar registro');

    const confirm = wrapper
      .findAllComponents({ name: 'QBtn' })
      .filter((button) => button.props('label') === 'Restaurar')
      .at(-1)!;
    await confirm.trigger('click');
    await flushPromises();

    expect(mocks.restore).toHaveBeenCalledWith(7);
    expect(mocks.list).toHaveBeenCalledTimes(2);
    expect(mocks.success).toHaveBeenCalledWith('«Taladro» volvió al inventario.');
  });

  it('says the record was already restored on a 404 and refreshes the list', async () => {
    mocks.list.mockResolvedValue(page([entry()]));
    mocks.restore.mockRejectedValue(new ApiError(404, 'Not Found'));

    const wrapper = await mountPanel();
    await restoreButton(rowAt(wrapper, 0)).trigger('click');
    const confirm = wrapper
      .findAllComponents({ name: 'QBtn' })
      .filter((button) => button.props('label') === 'Restaurar')
      .at(-1)!;
    await confirm.trigger('click');
    await flushPromises();

    expect(wrapper.text()).toContain('ya estaba restaurado');
    expect(mocks.list).toHaveBeenCalledTimes(2);
    expect(mocks.error).not.toHaveBeenCalled();
  });

  it('shows the API message when the restore fails for another reason', async () => {
    mocks.list.mockResolvedValue(page([entry()]));
    mocks.restore.mockRejectedValue(new ApiError(403, 'Forbidden', undefined, 'No autorizado'));

    const wrapper = await mountPanel();
    await restoreButton(rowAt(wrapper, 0)).trigger('click');
    const confirm = wrapper
      .findAllComponents({ name: 'QBtn' })
      .filter((button) => button.props('label') === 'Restaurar')
      .at(-1)!;
    await confirm.trigger('click');
    await flushPromises();

    expect(wrapper.text()).toContain('No autorizado');
    expect(mocks.error).toHaveBeenCalled();
    expect(mocks.list).toHaveBeenCalledTimes(1);
  });

  it('reports a failed list in the banner instead of an empty table that looks like a bug', async () => {
    mocks.list.mockRejectedValue(
      new ApiError(500, 'Server Error', undefined, 'Archivo no disponible'),
    );

    const wrapper = await mountPanel();

    expect(wrapper.text()).toContain('Archivo no disponible');
    expect(wrapper.findAll('.q-table-row')).toHaveLength(0);
  });

  it('moves between pages with the total, not with a full page', async () => {
    mocks.list.mockResolvedValue({ items: [entry()], page: 1, pageSize: 25, total: 60 });

    const wrapper = await mountPanel();
    expect(wrapper.text()).toContain('página 1 de 3');

    const next = wrapper.find('button[aria-label="Página siguiente"]');
    await next.trigger('click');
    await flushPromises();

    expect(mocks.list).toHaveBeenLastCalledWith(expect.objectContaining({ page: 2 }));
  });
});
