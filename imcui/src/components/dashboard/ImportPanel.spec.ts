import { defineComponent, h, type PropType } from 'vue';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import ImportPanel from './ImportPanel.vue';
import { ApiError } from '@services/api';
import type { ImportOutcome, ImportPreview } from '@services/import';

// The panel talks to the API and to Quasar's notifications; both are replaced, so the spec sees the
// payload that would go out and the copy the user reads, not a network or a toast queue.
const mocks = vi.hoisted(() => ({
  preview: vi.fn(),
  run: vi.fn(),
  success: vi.fn(),
  error: vi.fn(),
  info: vi.fn(),
}));

vi.mock('@services/import', () => ({
  importService: { preview: mocks.preview, run: mocks.run },
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

const QFile = defineComponent({
  name: 'QFile',
  props: {
    label: { type: String, default: '' },
    accept: { type: String, default: undefined },
    disable: { type: Boolean, default: false },
    modelValue: { type: Object as PropType<File | null>, default: null },
  },
  emits: ['update:modelValue'],
  setup(props, { emit }) {
    return () =>
      h('input', {
        type: 'file',
        'data-label': props.label,
        'data-accept': props.accept,
        disabled: props.disable,
        onChange: (event: Event) =>
          emit('update:modelValue', (event.target as HTMLInputElement).files?.[0] ?? null),
      });
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
  setup(props, { emit }) {
    return () =>
      h(
        'button',
        {
          disabled: props.disable,
          onClick: (event: MouseEvent) => emit('click', event),
        },
        props.label || props.icon,
      );
  },
});

const QCheckbox = defineComponent({
  name: 'QCheckbox',
  props: {
    modelValue: { type: Boolean, default: false },
    disable: { type: Boolean, default: false },
    ariaLabel: { type: String, default: '' },
  },
  emits: ['update:modelValue'],
  setup(props, { emit, slots }) {
    return () =>
      h('label', { class: 'q-checkbox-stub' }, [
        h('input', {
          type: 'checkbox',
          checked: props.modelValue,
          disabled: props.disable,
          'aria-label': props.ariaLabel,
          onChange: () => emit('update:modelValue', !props.modelValue),
        }),
        slots.default?.(),
      ]);
  },
});

const QChip = defineComponent({
  name: 'QChip',
  props: {
    label: { type: String, default: '' },
    clickable: { type: Boolean, default: false },
    icon: { type: String, default: '' },
  },
  emits: ['click'],
  setup(props, { emit }) {
    return () =>
      h(
        'button',
        { class: 'q-chip-stub', onClick: () => emit('click') },
        props.label || props.icon,
      );
  },
});

const QInput = defineComponent({
  name: 'QInput',
  props: { modelValue: { type: String, default: '' }, label: { type: String, default: '' } },
  emits: ['update:modelValue'],
  setup(props, { emit }) {
    return () =>
      h('input', {
        class: 'q-input-stub',
        value: props.modelValue,
        'data-label': props.label,
        onInput: (event: Event) =>
          emit('update:modelValue', (event.target as HTMLInputElement).value),
      });
  },
});

function passthrough(name: string) {
  return defineComponent({
    name,
    setup(_props, { slots }) {
      return () => h('div', { class: `${name}-stub` }, slots.default?.());
    },
  });
}

const QBadge = defineComponent({
  name: 'QBadge',
  props: { color: { type: String, default: '' } },
  setup(props, { slots }) {
    return () => h('span', { class: 'q-badge-stub', 'data-color': props.color }, slots.default?.());
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

const stubs = {
  QFile,
  QBtn,
  QCheckbox,
  QChip,
  QInput,
  QBadge,
  QBanner,
  QIcon,
  QCard: passthrough('QCard'),
  QCardSection: passthrough('QCardSection'),
  QSpace: defineComponent({ name: 'QSpace', render: () => h('span', { class: 'q-space' }) }),
  QTooltip: defineComponent({
    name: 'QTooltip',
    setup(_props, { slots }) {
      return () => h('span', { class: 'q-tooltip-stub' }, slots.default?.());
    },
  }),
};

/**
 * One sheet, one group, four rows: a green one, two yellows (one of them without a code) and a red
 * one. Every nullable field is null where the API would have omitted it.
 */
function previewFixture(): ImportPreview {
  return {
    sheets: [
      {
        name: 'BACKLINE',
        status: 'red',
        green: 1,
        yellow: 2,
        red: 1,
        skippedSections: 2,
        groups: [
          {
            name: 'BASE TIPO BOOM',
            status: 'red',
            green: 1,
            yellow: 2,
            red: 1,
            items: [
              {
                key: 'BACKLINE#12',
                label: 'Base de brazo DW 9000',
                code: '-H0001',
                brand: 'DW',
                reference: '9700',
                serial: null,
                notes: null,
                dimensions: null,
                status: 'green',
                reasons: [],
                row: 12,
              },
              {
                key: 'BACKLINE#13',
                label: 'Cuerpo de bombo',
                code: '-H0002',
                brand: 'Pearl',
                reference: null,
                serial: null,
                notes: null,
                dimensions: null,
                status: 'yellow',
                reasons: ['Sin dimensiones declaradas'],
                row: 13,
              },
              {
                key: 'BACKLINE#14',
                label: 'Parche sin código',
                code: null,
                brand: null,
                reference: null,
                serial: null,
                notes: null,
                dimensions: null,
                status: 'yellow',
                reasons: ['Falta el número de parte'],
                row: 14,
              },
              {
                key: 'BACKLINE#15',
                label: 'Soporte roto',
                code: '-H0004',
                brand: 'DW',
                reference: null,
                serial: null,
                notes: null,
                dimensions: null,
                status: 'red',
                reasons: ['Código duplicado en la hoja'],
                row: 15,
              },
            ],
          },
        ],
      },
    ],
    summary: { rows: 4, sections: 2, green: 1, yellow: 2, red: 1, importable: 3, skipped: 1 },
    issues: [{ sheet: 'BACKLINE', row: 15, level: 'red', message: 'Código duplicado' }],
    normalizations: ['«GTAR COMBO» → «GTR COMBO»'],
  };
}

function outcomeFixture(overrides: Partial<ImportOutcome> = {}): ImportOutcome {
  return {
    createdSheets: 1,
    createdGroups: 1,
    createdItems: 3,
    skippedRed: 1,
    skippedYellow: 0,
    skippedExisting: 2,
    includeYellow: true,
    ...overrides,
  };
}

async function mountPanel(mobile = false): Promise<VueWrapper> {
  const wrapper = mount(ImportPanel, {
    global: {
      stubs,
      // `$q` is a Quasar plugin global property, injected the way every spec provides one.
      mocks: { $q: { screen: { lt: { sm: mobile } } } },
    },
  });
  await flushPromises();
  return wrapper;
}

function button(wrapper: VueWrapper, startsWith: string) {
  const found = wrapper
    .findAllComponents({ name: 'QBtn' })
    .find((candidate) => String(candidate.props('label') ?? '').startsWith(startsWith));
  if (!found) throw new Error(`no button whose label starts with ${startsWith}`);
  return found;
}

function checkbox(wrapper: VueWrapper, label: string) {
  const found = wrapper
    .findAllComponents({ name: 'QCheckbox' })
    .find((candidate) => candidate.props('ariaLabel') === `Importar ${label}`);
  if (!found) throw new Error(`no checkbox for ${label}`);
  return found;
}

function chip(wrapper: VueWrapper, label: string) {
  const found = wrapper
    .findAllComponents({ name: 'QChip' })
    .find((candidate) => String(candidate.props('label') ?? '').startsWith(label));
  if (!found) throw new Error(`no chip labelled ${label}`);
  return found;
}

function itemRows(wrapper: VueWrapper) {
  return wrapper.findAll('.import-item');
}

/** Picks the file the way the QFile stub would, then presses Analizar and waits for the tree. */
async function analyse(wrapper: VueWrapper, file = new File(['x'.repeat(12800)], 'stock.xlsx')) {
  wrapper.findComponent({ name: 'QFile' }).vm.$emit('update:modelValue', file);
  await flushPromises();
  await button(wrapper, 'Analizar').trigger('click');
  await flushPromises();
  return file;
}

beforeEach(() => {
  localStorage.clear();
  mocks.preview.mockReset();
  mocks.run.mockReset();
  mocks.success.mockReset();
  mocks.error.mockReset();
  mocks.info.mockReset();
  mocks.preview.mockResolvedValue(previewFixture());
  mocks.run.mockResolvedValue(outcomeFixture());
});

describe('ImportPanel — reading the file', () => {
  it('cannot analyse before a file is picked, and shows the name and size once it is', async () => {
    const wrapper = await mountPanel();

    expect(button(wrapper, 'Analizar').props('disable')).toBe(true);
    expect(wrapper.text()).not.toContain('12.5 KB');

    const file = new File(['x'.repeat(12800)], 'stock.xlsx');
    wrapper.findComponent({ name: 'QFile' }).vm.$emit('update:modelValue', file);
    await flushPromises();

    expect(button(wrapper, 'Analizar').props('disable')).toBe(false);
    expect(wrapper.find('.file-name').text()).toBe('stock.xlsx');
    expect(wrapper.find('.file-size').text()).toContain('12.5 KB');
    expect(mocks.preview).not.toHaveBeenCalled();
  });

  it('offers the formats the API reads in the picker', async () => {
    const wrapper = await mountPanel();

    expect(wrapper.findComponent({ name: 'QFile' }).props('accept')).toBe('.xlsx,.xls,.csv');
  });

  it('analyses the picked file and renders the tree the API answered with', async () => {
    const wrapper = await mountPanel();
    const file = await analyse(wrapper);

    expect(mocks.preview).toHaveBeenCalledWith(file);
    expect(wrapper.find('.sheet-name').text()).toBe('BACKLINE');
    expect(wrapper.find('.group-name').text()).toBe('BASE TIPO BOOM');
    expect(itemRows(wrapper)).toHaveLength(4);
  });

  it('reports a file the API could not read instead of an empty tree', async () => {
    mocks.preview.mockRejectedValue(
      new ApiError(
        400,
        'Bad Request',
        undefined,
        'The file could not be read as a spreadsheet. Save it again as .xlsx and retry.',
      ),
    );

    const wrapper = await mountPanel();
    wrapper
      .findComponent({ name: 'QFile' })
      .vm.$emit('update:modelValue', new File(['x'], 'a.xlsx'));
    await flushPromises();
    await button(wrapper, 'Analizar').trigger('click');
    await flushPromises();

    expect(wrapper.find('.q-banner-stub').text()).toContain('could not be read as a spreadsheet');
    expect(itemRows(wrapper)).toHaveLength(0);
    expect(mocks.error).toHaveBeenCalled();
  });
});

describe('ImportPanel — the tree', () => {
  it('carries the three counts and a coloured marker at sheet and group level', async () => {
    const wrapper = await mountPanel();
    await analyse(wrapper);

    const sheet = wrapper.find('.sheet-row');
    expect(sheet.find('.count-green').text()).toBe('1');
    expect(sheet.find('.count-yellow').text()).toBe('2');
    expect(sheet.find('.count-red').text()).toBe('1');
    // Any red in what is shown makes the level red, the API's own rule.
    expect(sheet.find('.status-marker').text()).toBe('rojo');
    expect(sheet.find('.status-marker').attributes('data-color')).toBe('negative');
    expect(sheet.text()).toContain('2 títulos de sección');

    const group = wrapper.find('.group-row');
    expect(group.find('.count-green').text()).toBe('1');
    expect(group.find('.count-yellow').text()).toBe('2');
    expect(group.find('.count-red').text()).toBe('1');
    expect(group.find('.status-marker').attributes('data-color')).toBe('negative');
  });

  it('shows each row with its label, code, brand and colour', async () => {
    const wrapper = await mountPanel();
    await analyse(wrapper);

    const [green, yellow, codeless, red] = itemRows(wrapper).map((row) => row.text());

    expect(green).toContain('Base de brazo DW 9000');
    expect(green).toContain('-H0001');
    expect(green).toContain('DW');
    expect(green).toContain('verde');

    expect(yellow).toContain('Cuerpo de bombo');
    expect(yellow).toContain('-H0002');
    expect(yellow).toContain('amarillo');

    expect(codeless).toContain('sin código');
    expect(red).toContain('Soporte roto');
    expect(red).toContain('rojo');
    expect(red).toContain('fila 15');
  });

  it('shows the reasons on a yellow and a red row, and none on a green one', async () => {
    const wrapper = await mountPanel();
    await analyse(wrapper);

    const rows = itemRows(wrapper);
    expect(rows[0]!.find('.item-reasons').exists()).toBe(false);
    expect(rows[1]!.find('.item-reasons').text()).toBe('Sin dimensiones declaradas');
    expect(rows[2]!.find('.item-reasons').text()).toBe('Falta el número de parte');
    expect(rows[3]!.find('.item-reasons').text()).toBe('Código duplicado en la hoja');
  });

  it('badges each level with the colour its counts produce', async () => {
    mocks.preview.mockResolvedValue({
      ...previewFixture(),
      sheets: [
        {
          name: 'HARDWARE',
          status: 'green',
          green: 2,
          yellow: 0,
          red: 0,
          skippedSections: 0,
          groups: [
            {
              name: 'TORNILLERÍA',
              status: 'green',
              green: 2,
              yellow: 0,
              red: 0,
              items: [
                {
                  key: 'HARDWARE#3',
                  label: 'Tornillo M6',
                  code: '-T0001',
                  brand: null,
                  reference: null,
                  serial: null,
                  notes: null,
                  dimensions: null,
                  status: 'green',
                  reasons: [],
                  row: 3,
                },
                {
                  key: 'HARDWARE#4',
                  label: 'Tornillo M8',
                  code: '-T0002',
                  brand: null,
                  reference: null,
                  serial: null,
                  notes: null,
                  dimensions: null,
                  status: 'green',
                  reasons: [],
                  row: 4,
                },
              ],
            },
          ],
        },
      ],
      summary: { rows: 2, sections: 0, green: 2, yellow: 0, red: 0, importable: 2, skipped: 0 },
      issues: [],
    });

    const wrapper = await mountPanel();
    await analyse(wrapper);

    expect(wrapper.find('.sheet-row .status-marker').text()).toBe('verde');
    expect(wrapper.find('.sheet-row .status-marker').attributes('data-color')).toBe('positive');
  });
});

describe('ImportPanel — filters', () => {
  it('narrows the tree to the reds and counts only what is shown', async () => {
    const wrapper = await mountPanel();
    await analyse(wrapper);

    await chip(wrapper, 'Solo rojos').trigger('click');
    await flushPromises();

    expect(itemRows(wrapper)).toHaveLength(1);
    expect(itemRows(wrapper)[0]!.text()).toContain('Soporte roto');
    const sheet = wrapper.find('.sheet-row');
    expect(sheet.find('.count-green').text()).toBe('0');
    expect(sheet.find('.count-yellow').text()).toBe('0');
    expect(sheet.find('.count-red').text()).toBe('1');
    expect(sheet.find('.status-marker').text()).toBe('rojo');
  });

  it('filters by code presence, and says so on the chip', async () => {
    const wrapper = await mountPanel();
    await analyse(wrapper);

    expect(chip(wrapper, 'Con código').text()).toBe('Con código (3)');
    expect(chip(wrapper, 'Sin código').text()).toBe('Sin código (1)');

    await chip(wrapper, 'Sin código').trigger('click');
    await flushPromises();

    expect(itemRows(wrapper)).toHaveLength(1);
    expect(itemRows(wrapper)[0]!.text()).toContain('Parche sin código');
  });

  it('searches sheet, group and label with one text box', async () => {
    const wrapper = await mountPanel();
    await analyse(wrapper);

    const search = wrapper.findComponent({ name: 'QInput' });
    search.vm.$emit('update:modelValue', 'cuerpo');
    await flushPromises();
    expect(itemRows(wrapper)).toHaveLength(1);
    expect(itemRows(wrapper)[0]!.text()).toContain('Cuerpo de bombo');

    search.vm.$emit('update:modelValue', 'base tipo boom');
    await flushPromises();
    expect(itemRows(wrapper)).toHaveLength(4);

    search.vm.$emit('update:modelValue', 'nada');
    await flushPromises();
    expect(wrapper.find('.no-matches').exists()).toBe(true);
  });
});

describe('ImportPanel — the import', () => {
  it('counts the greens plus the ticked yellows, never the reds', async () => {
    const wrapper = await mountPanel();
    await analyse(wrapper);

    // One green and two yellows, both ticked by default.
    expect(button(wrapper, 'Importar').text()).toBe('Importar 3 elementos');
    expect(button(wrapper, 'Importar').props('disable')).toBe(false);
  });

  it('unticking a yellow adds its code to skipCodes and lowers the count', async () => {
    const wrapper = await mountPanel();
    await analyse(wrapper);

    await checkbox(wrapper, 'Cuerpo de bombo').find('input').trigger('change');
    await flushPromises();

    expect(button(wrapper, 'Importar').text()).toBe('Importar 2 elementos');

    await button(wrapper, 'Importar').trigger('click');
    await flushPromises();

    expect(mocks.run).toHaveBeenCalledWith(expect.any(File), {
      includeYellow: true,
      skipCodes: ['-H0002'],
    });
    // The API counts classes, not the single codes the user unticked: the panel shows them back.
    expect(wrapper.find('.result-skipped').text()).toContain('Destildados aquí: 1');
  });

  it('cannot untick a yellow without a code, and says why instead of pretending', async () => {
    const wrapper = await mountPanel();
    await analyse(wrapper);

    const codeless = checkbox(wrapper, 'Parche sin código');

    expect(codeless.props('disable')).toBe(true);
    expect(codeless.find('.q-tooltip-stub').text()).toContain('por código');
    // It stays counted: a row the API can only drop by code cannot be dropped one by one.
    expect(button(wrapper, 'Importar').text()).toBe('Importar 3 elementos');
  });

  it('never puts a red row in the payload, even when every yellow is unticked', async () => {
    const wrapper = await mountPanel();
    await analyse(wrapper);

    await checkbox(wrapper, 'Cuerpo de bombo').find('input').trigger('change');
    // The codeless yellow cannot be unticked, so includeYellow stays true: it is still selected.
    await flushPromises();
    await button(wrapper, 'Importar').trigger('click');
    await flushPromises();

    const [, request] = mocks.run.mock.calls[0] as [
      File,
      { includeYellow: boolean; skipCodes: string[] },
    ];
    expect(request.skipCodes).toEqual(['-H0002']);
    expect(request.skipCodes).not.toContain('-H0004');
    expect(request).toEqual({ includeYellow: true, skipCodes: ['-H0002'] });
  });

  it('sends includeYellow false when no yellow row is left ticked', async () => {
    mocks.preview.mockResolvedValue({
      ...previewFixture(),
      sheets: [
        {
          ...previewFixture().sheets[0]!,
          groups: [
            {
              name: 'BASE TIPO BOOM',
              status: 'yellow',
              green: 1,
              yellow: 1,
              red: 0,
              items: [
                previewFixture().sheets[0]!.groups[0]!.items[0]!,
                previewFixture().sheets[0]!.groups[0]!.items[1]!,
              ],
            },
          ],
        },
      ],
    });

    const wrapper = await mountPanel();
    await analyse(wrapper);
    await checkbox(wrapper, 'Cuerpo de bombo').find('input').trigger('change');
    await flushPromises();

    await button(wrapper, 'Importar').trigger('click');
    await flushPromises();

    expect(mocks.run).toHaveBeenCalledWith(expect.any(File), {
      includeYellow: false,
      skipCodes: ['-H0002'],
    });
  });

  it('disables the button when there is nothing green or yellow to send', async () => {
    mocks.preview.mockResolvedValue({
      ...previewFixture(),
      summary: { rows: 1, sections: 0, green: 0, yellow: 0, red: 1, importable: 0, skipped: 1 },
      issues: [],
      sheets: [
        {
          name: 'BACKLINE',
          status: 'red',
          green: 0,
          yellow: 0,
          red: 1,
          skippedSections: 0,
          groups: [
            {
              name: 'BASE TIPO BOOM',
              status: 'red',
              green: 0,
              yellow: 0,
              red: 1,
              items: [previewFixture().sheets[0]!.groups[0]!.items[3]!],
            },
          ],
        },
      ],
    });

    const wrapper = await mountPanel();
    await analyse(wrapper);

    expect(button(wrapper, 'Importar').text()).toBe('Importar 0 elementos');
    expect(button(wrapper, 'Importar').props('disable')).toBe(true);
  });

  it('shows what the import did and tells the host page', async () => {
    const wrapper = await mountPanel();
    await analyse(wrapper);

    await button(wrapper, 'Importar').trigger('click');
    await flushPromises();

    const result = wrapper.find('.result-card');
    expect(result.text()).toContain('Importación terminada');
    expect(result.find('.result-created').text()).toBe('3');
    expect(result.text()).toContain('Rojos omitidos: 1');
    expect(result.text()).toContain('Amarillos omitidos: 0');
    expect(result.text()).toContain('Ya existían: 2');
    expect(result.text()).toContain('Destildados aquí: 0');
    expect(mocks.success).toHaveBeenCalledWith('Se importaron 3 elementos.');
    expect(wrapper.emitted('imported')).toEqual([[outcomeFixture()]]);
  });

  it('reports a refused import and stays on the tree', async () => {
    mocks.run.mockRejectedValue(
      new ApiError(400, 'Bad Request', undefined, 'Nothing was written: the import failed.'),
    );

    const wrapper = await mountPanel();
    await analyse(wrapper);
    await button(wrapper, 'Importar').trigger('click');
    await flushPromises();

    expect(wrapper.find('.q-banner-stub').text()).toContain('Nothing was written');
    expect(wrapper.find('.result-card').exists()).toBe(false);
    expect(itemRows(wrapper)).toHaveLength(4);
    expect(mocks.error).toHaveBeenCalled();
  });
});

describe('ImportPanel — below sm', () => {
  it('renders every row as a card, stacked, with the same data', async () => {
    const wrapper = await mountPanel(true);
    await analyse(wrapper);

    expect(wrapper.findAll('.import-row')).toHaveLength(0);
    const cards = wrapper.findAll('.import-card');
    expect(cards).toHaveLength(4);
    expect(cards[1]!.text()).toContain('Cuerpo de bombo');
    expect(cards[1]!.text()).toContain('Sin dimensiones declaradas');
    expect(cards[1]!.find('.q-checkbox-stub').exists()).toBe(true);

    expect(wrapper.find('.filters').classes()).toContain('column');
    expect(wrapper.find('.filter-chips').classes()).toContain('column');
    expect(wrapper.find('.import-button').classes()).toContain('full-width');
  });

  it('keeps the line layout at sm and above', async () => {
    const wrapper = await mountPanel(false);
    await analyse(wrapper);

    expect(wrapper.findAll('.import-card')).toHaveLength(0);
    expect(wrapper.findAll('.import-row')).toHaveLength(4);
    expect(wrapper.find('.filters').classes()).toContain('row');
  });

  it('imports from a card with the same payload as the desktop line', async () => {
    const wrapper = await mountPanel(true);
    await analyse(wrapper);

    await wrapper.find('input[aria-label="Importar Cuerpo de bombo"]').trigger('change');
    await flushPromises();
    await button(wrapper, 'Importar').trigger('click');
    await flushPromises();

    expect(mocks.run).toHaveBeenCalledWith(expect.any(File), {
      includeYellow: true,
      skipCodes: ['-H0002'],
    });
  });
});
