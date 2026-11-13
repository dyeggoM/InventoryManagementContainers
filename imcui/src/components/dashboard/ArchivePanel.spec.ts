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

const QCardSection = defineComponent({
  name: 'QCardSection',
  setup(_props, { slots }) {
    return () => h('div', { class: 'q-card-section-stub' }, slots.default?.());
  },
});

const QCardActions = defineComponent({
  name: 'QCardActions',
  setup(_props, { slots }) {
    return () => h('div', { class: 'q-card-actions-stub' }, slots.default?.());
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
  QCardSection,
  QCardActions,
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

async function mountPanel(mobile = false): Promise<VueWrapper> {
  const wrapper = mount(ArchivePanel, {
    global: {
      stubs,
      // `$q` is a Quasar plugin global property, injected the way every spec provides one.
      mocks: { $q: { screen: { lt: { sm: mobile } } } },
    },
  });
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

  it('renders the archive as cards below sm, with Restaurar inside the card', async () => {
    mocks.list.mockResolvedValue(page([entry({ attachmentsJson: '[{"id":1}]' })]));
    mocks.restore.mockResolvedValue({ id: 7, label: 'Taladro' });

    const wrapper = await mountPanel(true);

    // Six columns do not fit a phone: the row becomes a card and the table is not rendered.
    expect(wrapper.find('.q-table-stub').exists()).toBe(false);

    const card = wrapper.find('.archive-card');
    expect(card.exists()).toBe(true);
    expect(card.text()).toContain('Taladro');
    expect(card.text()).toContain('#7');
    expect(card.text()).toContain(new Date('2026-11-10T12:30:00Z').toLocaleString());
    expect(card.text()).toContain('root@imc.test');
    expect(card.text()).toContain('En archivo');
    expect(card.find('[aria-label="Tenía adjuntos"]').exists()).toBe(true);

    // The action is in the card, and it still reaches the service after the confirmation.
    await card.find('button[aria-label="Restaurar Taladro"]').trigger('click');
    expect(mocks.restore).not.toHaveBeenCalled();
    const confirm = wrapper
      .findAllComponents({ name: 'QBtn' })
      .filter((button) => button.props('label') === 'Restaurar')
      .at(-1)!;
    await confirm.trigger('click');
    await flushPromises();

    expect(mocks.restore).toHaveBeenCalledWith(7);
  });

  it('keeps the table and hides the cards at sm and above', async () => {
    mocks.list.mockResolvedValue(page([entry()]));

    const wrapper = await mountPanel(false);

    expect(wrapper.find('.q-table-stub').exists()).toBe(true);
    expect(wrapper.find('.archive-card').exists()).toBe(false);
  });

  it('stacks the filters and their buttons on a phone, and not above sm', async () => {
    mocks.list.mockResolvedValue(page([]));

    const mobile = await mountPanel(true);
    expect(mobile.find('.filters').classes()).toContain('column');
    for (const field of mobile.findAll('.filters > div')) {
      // Each control takes the full width instead of two-per-row at 50%.
      expect(field.classes()).toContain('full-width');
    }
    expect(mobile.find('.filter-actions').classes()).toContain('full-width');

    const desktop = await mountPanel(false);
    expect(desktop.find('.filters').classes()).toContain('row');
    expect(desktop.findAll('.filters > div')[0]!.classes()).toContain('col-md-3');
  });

  it('keeps the first page total when a later page answers without one', async () => {
    mocks.list
      .mockResolvedValueOnce({ items: [entry()], page: 1, pageSize: 25, total: 60 })
      .mockResolvedValueOnce({ items: [entry()], page: 2, pageSize: 25, total: null });

    const wrapper = await mountPanel(true);
    expect(wrapper.text()).toContain('página 1 de 3');

    await wrapper.find('button[aria-label="Página siguiente"]').trigger('click');
    await flushPromises();

    // A null total must not turn page 2 into "página 2 de 1", and the pager keeps working.
    expect(wrapper.text()).toContain('página 2 de 3');
    expect(
      wrapper.find('button[aria-label="Página siguiente"]').attributes('disabled'),
    ).toBeUndefined();
  });
});
