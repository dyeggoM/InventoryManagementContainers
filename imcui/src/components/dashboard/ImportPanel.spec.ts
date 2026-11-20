import { defineComponent, h, type PropType } from 'vue';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia } from 'pinia';
import type * as ImportServiceModule from '@services/import';
import ImportPanel from './ImportPanel.vue';
import AttributeEditor from '../AttributeEditor.vue';
import { ApiError } from '@services/api';
import type { Attributes } from '@services/items';
import type * as ConstantsModule from '@services/constants';
import type {
  ImportCorrection,
  ImportOutcome,
  ImportPreview,
  ImportSuggestion,
} from '@services/import';

// The panel talks to the API and to Quasar's notifications; both are replaced, so the spec sees the
// payload that would go out and the copy the user reads, not a network or a toast queue. The
// attribute catalogue is Configuration's, and the dialog reads it on mount: it is stubbed too, with
// the labels the real database defines, so the form under test is the one the app builds.
const mocks = vi.hoisted(() => ({
  preview: vi.fn(),
  run: vi.fn(),
  success: vi.fn(),
  error: vi.fn(),
  info: vi.fn(),
  getByGroup: vi.fn(),
  getAll: vi.fn(),
  createConstant: vi.fn(),
}));

vi.mock('@services/import', async (importOriginal) => {
  // The pure helpers (`subcategoryFromRaw`) stay real: only the two calls go to the mock.
  const actual = await importOriginal<typeof ImportServiceModule>();
  return { ...actual, importService: { preview: mocks.preview, run: mocks.run } };
});

vi.mock('@services/constants', async (importOriginal) => {
  // `codeOf`/`entryOf` stay real: they are the rule that reads a code out of a constant, and
  // mocking them would test the mock instead of the panel.
  const actual = await importOriginal<typeof ConstantsModule>();
  return {
    ...actual,
    constantsService: {
      getByGroup: mocks.getByGroup,
      getAll: mocks.getAll,
      create: mocks.createConstant,
    },
  };
});

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
    hint: { type: String, default: '' },
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
        'data-hint': props.hint,
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
    ariaLabel: { type: String, default: '' },
  },
  emits: ['click'],
  setup(props, { emit }) {
    return () =>
      h(
        'button',
        {
          disabled: props.disable,
          'aria-label': props.ariaLabel || undefined,
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

const QSelect = defineComponent({
  name: 'QSelect',
  props: {
    modelValue: { type: [String, Number], default: '' },
    label: { type: String, default: '' },
    options: { type: Array as PropType<unknown[]>, default: () => [] },
    displayValue: { type: String, default: '' },
    placeholder: { type: String, default: '' },
    hint: { type: String, default: '' },
    useInput: { type: Boolean, default: false },
    hideSelected: { type: Boolean, default: false },
    fillInput: { type: Boolean, default: true },
    inputDebounce: { type: [Number, String], default: 500 },
    optionsDense: { type: Boolean, default: false },
    disable: { type: Boolean, default: false },
    loading: { type: Boolean, default: false },
    readonly: { type: Boolean, default: false },
  },
  emits: ['update:modelValue', 'filter', 'popup-hide'],
  setup(props, { slots }) {
    return () =>
      h('div', { class: 'q-select-stub', 'data-label': props.label }, [
        h(
          'span',
          { class: 'q-select-stub__display' },
          props.displayValue || String(props.modelValue ?? ''),
        ),
        h(
          'ul',
          { class: 'q-select-stub__options' },
          props.options.map((option) =>
            h('li', { class: 'q-select-stub__option' }, optionLabel(option)),
          ),
        ),
        // Quasar shows `no-option` only when nothing matches, and the attribute rows fill it with
        // their own items: rendering it always would mount controls this spec does not stub.
        props.options.length === 0 ? (slots['no-option']?.({ inputValue: '' }) ?? null) : null,
      ]);
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

/** Renders its content only while open, which is what "the dialog is closed" has to mean here. */
const QDialog = defineComponent({
  name: 'QDialog',
  props: {
    modelValue: { type: Boolean, default: false },
    maximized: { type: Boolean, default: false },
  },
  emits: ['update:modelValue'],
  setup(props, { slots }) {
    return () =>
      props.modelValue
        ? h(
            'div',
            { class: 'q-dialog-stub', 'data-maximized': String(props.maximized) },
            slots.default?.(),
          )
        : null;
  },
});

/** Options are objects carrying a `label` or plain strings, which is what both callers pass. */
function optionLabel(option: unknown): string {
  if (typeof option === 'string') return option;
  if (option && typeof option === 'object' && 'label' in option) {
    const label = (option as { label?: unknown }).label;
    return typeof label === 'string' ? label : '';
  }
  return '';
}

const stubs = {
  QFile,
  QBtn,
  QCheckbox,
  QChip,
  QInput,
  QSelect,
  QBadge,
  QBanner,
  QIcon,
  QDialog,
  QCard: passthrough('QCard'),
  QCardSection: passthrough('QCardSection'),
  QCardActions: passthrough('QCardActions'),
  // The attribute editor's rows carry these; the panel only hosts it.
  QItem: passthrough('QItem'),
  QItemSection: passthrough('QItemSection'),
  QSpinner: defineComponent({ name: 'QSpinner', render: () => h('span', { class: 'q-spinner' }) }),
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
                source: 'BACKLINE',
                raw: {
                  CONSECUTIVO: '-H0002',
                  'SUB CATEGORIA': 'BASE TIPO BOOM',
                  DESCRIPCION: 'Cuerpo de bombo',
                  MARCA: 'Pearl',
                  DIMENSIONES: '',
                },
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
                source: 'BACKLINE',
                raw: {
                  CONSECUTIVO: '-H0004',
                  'SUB CATEGORIA': 'BASE TIPO BOOM',
                  DESCRIPCION: 'Soporte roto',
                  MARCA: 'DW',
                  DIMENSIONES: '',
                },
              },
            ],
          },
        ],
      },
    ],
    summary: { rows: 4, sections: 2, green: 1, yellow: 2, red: 1, importable: 3, skipped: 1 },
    issues: [{ sheet: 'BACKLINE', row: 15, level: 'red', message: 'Código duplicado' }],
    normalizations: ['«GTAR COMBO» → «GTR COMBO»'],
    sourceHeaders: {
      BACKLINE: ['CONSECUTIVO', 'SUB CATEGORIA', 'DESCRIPCION', 'MARCA', 'DIMENSIONES'],
    },
    correctionIssues: [],
  };
}

/**
 * The same tree, with the group the API placed by a rule: it carries the line that says why, and
 * every other field stays exactly as in the fixture above.
 */
function previewWithNote(note: string): ImportPreview {
  const preview = previewFixture();
  const group = preview.sheets[0]!.groups[0]!;
  return {
    ...preview,
    sheets: [
      {
        ...preview.sheets[0]!,
        groups: [{ ...group, note }],
      },
    ],
  };
}

/**
 * The group the API placed by similarity: the note explains where it went and the suggestion says
 * how close the match was. The group's own name is the proposed label — that is the real contract.
 */
function previewWithSuggestion(note: string, suggestion: ImportSuggestion): ImportPreview {
  const preview = previewWithNote(note);
  const group = preview.sheets[0]!.groups[0]!;
  return {
    ...preview,
    sheets: [{ ...preview.sheets[0]!, groups: [{ ...group, suggestion }] }],
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
    corrected: 0,
    correctionIssues: [],
    ...overrides,
  };
}

/**
 * The answer a corrected red row comes back as: the row the message fixed is green, in its group,
 * and it still carries the file's row for the editor's top half. The server decides the colour, so
 * the panel only has to render this.
 */
function correctedPreview(label = 'CONGA LP GALAXY 11 3/4'): ImportPreview {
  const preview = previewFixture();
  const sheet = preview.sheets[0]!;
  const group = sheet.groups[0]!;
  const red = group.items[3]!;
  return {
    ...preview,
    sheets: [
      {
        ...sheet,
        status: 'yellow',
        green: 2,
        red: 0,
        groups: [
          {
            ...group,
            status: 'yellow',
            green: 2,
            red: 0,
            items: [
              ...group.items.slice(0, 3),
              { ...red, label, status: 'green', reasons: [], raw: null },
            ],
          },
        ],
      },
    ],
    summary: { ...preview.summary, green: 2, red: 0, importable: 4, skipped: 0 },
  };
}

async function mountPanel(mobile = false): Promise<VueWrapper> {
  const wrapper = mount(ImportPanel, {
    global: {
      stubs,
      // The panel reads who is signed in: adding a value to Configuration is an admin's write.
      plugins: [createPinia()],
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

/** One field of the editor, found by the label the user reads above it. */
function input(wrapper: VueWrapper, label: string) {
  const found = wrapper
    .findAllComponents({ name: 'QInput' })
    .find((candidate) => candidate.props('label') === label);
  if (!found) throw new Error(`no input labelled ${label}`);
  return found;
}

function itemRows(wrapper: VueWrapper) {
  return wrapper.findAll('.import-item');
}

/** A select by its label, the way the dialog words it. */
function select(wrapper: VueWrapper, label: string) {
  const found = wrapper
    .findAllComponents({ name: 'QSelect' })
    .find((candidate) => candidate.props('label') === label);
  if (!found) throw new Error(`no select labelled ${label}`);
  return found;
}

/** The attribute rows the dialog is editing: the shared editor's model, not a copy of the DOM. */
function attributeRows(wrapper: VueWrapper): Attributes[] {
  return (wrapper.findComponent(AttributeEditor).props() as { modelValue: Attributes[] })
    .modelValue;
}

/** One attribute row's value, by the code the item stores. */
function attributeRow(rows: Attributes[], code: string): string | undefined {
  return rows.find((row) => row.code === code)?.value;
}

/** Replaces the attribute rows the way the editor does: it emits the model it was given. */
async function setAttributeRows(wrapper: VueWrapper, rows: Attributes[]): Promise<void> {
  wrapper.findComponent(AttributeEditor).vm.$emit('update:modelValue', rows);
  await flushPromises();
}

/** Types one attribute's value, leaving the rest of the rows as they were. */
async function setAttribute(wrapper: VueWrapper, code: string, value: string): Promise<void> {
  const rows = attributeRows(wrapper).map((row) => (row.code === code ? { ...row, value } : row));
  await setAttributeRows(wrapper, rows);
}

/** Picks the file the way the QFile stub would, then presses Analizar and waits for the tree. */
async function analyse(wrapper: VueWrapper, file = new File(['x'.repeat(12800)], 'stock.xlsx')) {
  wrapper.findComponent({ name: 'QFile' }).vm.$emit('update:modelValue', file);
  await flushPromises();
  await button(wrapper, 'Analizar').trigger('click');
  await flushPromises();
  return file;
}

/**
 * Configuration's attributes, as the real database defines them: `Brand` and `Notes` are configured,
 * `Description` is the one the name field carries, and `Code`, `Reference`, `Serial` and `Dimensions`
 * come from the importer alone. That mix is the interesting case — a form built only from the
 * catalogue would have no field for the code the file writes.
 */
function catalogueFixture(): void {
  mocks.getByGroup.mockResolvedValue([
    { id: 701, label: 'Brand', sortOrder: 0, dataType: 'Text' },
    { id: 708, label: 'Notes', sortOrder: 1, dataType: 'LongText' },
    { id: 707, label: 'Description', sortOrder: 2, dataType: 'LongText' },
    { id: 702, label: 'Category', sortOrder: 3, dataType: 'Text' },
  ]);
  mocks.getAll.mockResolvedValue({ tree: [], panels: [] });
}

beforeEach(() => {
  localStorage.clear();
  catalogueFixture();
  mocks.getByGroup.mockClear();
  mocks.getAll.mockClear();
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

    expect(mocks.preview).toHaveBeenCalledWith(file, []);
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

  it('shows the note under the group header when the API explains where a group landed', async () => {
    const note =
      '«CASE PARA CONGAS» no está en la configuración: se coloca en «Other» para revisarla.';
    mocks.preview.mockResolvedValue(previewWithNote(note));

    const wrapper = await mountPanel();
    await analyse(wrapper);

    const group = wrapper.find('.import-group');
    // It belongs to the header, not to a row: the explanation is about the group.
    expect(group.find('.group-row + .group-note').exists()).toBe(true);
    const line = group.find('.group-note');
    expect(line.text()).toBe(note);
    expect(line.classes()).toContain('text-caption');
    expect(itemRows(wrapper)).toHaveLength(4);

    // The filters narrow the rows, not the explanation: one visible row still carries it.
    wrapper.findComponent({ name: 'QInput' }).vm.$emit('update:modelValue', 'soporte');
    await flushPromises();
    expect(itemRows(wrapper)).toHaveLength(1);
    expect(wrapper.find('.group-note').text()).toBe(note);
  });

  it('shows no note line for a group the reader placed by its own name', async () => {
    const wrapper = await mountPanel();
    await analyse(wrapper);

    expect(wrapper.find('.group-note').exists()).toBe(false);
  });

  it('calls the top level a categoría, never a hoja', async () => {
    const wrapper = await mountPanel();

    expect(wrapper.findComponent({ name: 'QFile' }).attributes('data-hint')).toContain(
      'Una categoría por grupo raíz',
    );

    await analyse(wrapper);
    expect(wrapper.findComponent({ name: 'QInput' }).attributes('data-label')).toBe(
      'Categoría, grupo o elemento',
    );
    expect(wrapper.text()).not.toContain('hoja(s)');
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

describe('ImportPanel — the similarity suggestion', () => {
  const note =
    '«CONGA02» no está en la configuración; se propone «CONGA» (Percusion) por similitud (94%).';

  it('flags the proposal with a chip beside the name and keeps the note that explains it', async () => {
    mocks.preview.mockResolvedValue(
      previewWithSuggestion(note, { label: 'CONGA', category: 'Percusion', percent: 94 }),
    );

    const wrapper = await mountPanel();
    await analyse(wrapper);

    const group = wrapper.find('.import-group');
    const row = group.find('.group-row');
    const chipEl = row.find('.suggestion-chip');

    // The chip is in the header, next to the group name, and it says *sugerencia*: nothing was
    // renamed, the rows are already under that name.
    expect(chipEl.exists()).toBe(true);
    expect(chipEl.text()).toBe('Sugerencia · 94%');
    expect(row.find('.group-name').text()).toBe('BASE TIPO BOOM');
    // The explanation is still there — the chip flags, the note explains.
    expect(group.find('.group-note').text()).toBe(note);
    // A red group keeps both signals: the red count and the proposal, neither behind the other.
    expect(row.find('.count-red').text()).toBe('1');
    expect(row.find('.status-marker').text()).toBe('rojo');
    expect(row.find('.status-marker').attributes('data-color')).toBe('negative');
    // At desktop width the header stays on one line.
    expect(row.classes()).not.toContain('group-row--wrap');
  });

  it('renders no chip and no empty container for the groups that carry no proposal', async () => {
    // The common case, and the one that matters: only one of the two groups has a suggestion.
    const preview = previewWithSuggestion(note, {
      label: 'CONGA',
      category: 'Percusion',
      percent: 94,
    });
    const withSuggestion = preview.sheets[0]!.groups[0]!;
    mocks.preview.mockResolvedValue({
      ...preview,
      sheets: [
        {
          ...preview.sheets[0]!,
          groups: [
            withSuggestion,
            // Placed by its own name: no note, no suggestion, no chip.
            {
              ...withSuggestion,
              name: 'BASE TIPO BOOM II',
              note: null,
              suggestion: null,
            },
          ],
        },
      ],
    });

    const wrapper = await mountPanel();
    await analyse(wrapper);

    expect(wrapper.findAll('.suggestion-chip')).toHaveLength(1);
    const groups = wrapper.findAll('.import-group');
    expect(groups[0]!.find('.suggestion-chip').exists()).toBe(true);
    // No chip *and* no placeholder left behind: the second header holds only what it had before.
    expect(groups[1]!.find('.suggestion-chip').exists()).toBe(false);
    expect(groups[1]!.find('.group-note').exists()).toBe(false);
    expect(groups[1]!.find('.group-row').text()).not.toContain('Sugerencia');
    // Four children: icon, name, badge, counts. No chip, and no empty slot where one would be.
    expect(groups[1]!.findAll('.group-row > *')).toHaveLength(4);
    expect(groups[1]!.find('.group-name').text()).toBe('BASE TIPO BOOM II');
  });

  it('keeps the chip beside the name on a phone, wrapping the header instead of widening it', async () => {
    mocks.preview.mockResolvedValue(
      previewWithSuggestion(note, { label: 'CONGA', category: 'Percusion', percent: 94 }),
    );

    const wrapper = await mountPanel(true);
    await analyse(wrapper);

    const row = wrapper.find('.import-group .group-row');
    // Same header, now allowed to wrap: at 390px the chip drops under the name rather than
    // pushing the badge and the counts past the panel's edge.
    expect(row.classes()).toContain('group-row--wrap');
    expect(row.find('.suggestion-chip').text()).toBe('Sugerencia · 94%');
    expect(row.find('.count-red').text()).toBe('1');
    expect(wrapper.find('.import-group .group-note').text()).toBe(note);
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
    expect(result.text()).toContain('1 categoría(s) y 1 grupo(s) nuevos');
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

  it('keeps the note line under the header on a card layout too', async () => {
    mocks.preview.mockResolvedValue(
      previewWithNote('«CONGA DOBLE» se coloca en «BASE CONGA DOBLE»'),
    );

    const wrapper = await mountPanel(true);
    await analyse(wrapper);

    expect(wrapper.findAll('.import-card')).toHaveLength(4);
    expect(wrapper.find('.import-group .group-row + .group-note').text()).toBe(
      '«CONGA DOBLE» se coloca en «BASE CONGA DOBLE»',
    );
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

describe('ImportPanel — correcting a row', () => {
  /** Opens the fourth row's editor (the red one) and waits for the dialog to render. */
  async function openRedRow(wrapper: VueWrapper) {
    await itemRows(wrapper)[3]!.trigger('click');
    await flushPromises();
    return wrapper.find('.editor-card');
  }

  it('opens the file row for a red one: headers, values, and the editable item below', async () => {
    const wrapper = await mountPanel();
    await analyse(wrapper);

    // A green row has nothing to correct — it is already importable, so it opens nothing.
    await itemRows(wrapper)[0]!.trigger('click');
    expect(wrapper.find('.editor-card').exists()).toBe(false);

    const editor = await openRedRow(wrapper);
    expect(editor.exists()).toBe(true);
    // The top half is the file: its own header row, then the row itself, same column order.
    expect(wrapper.findAll('.file-header').map((cell) => cell.text())).toEqual([
      'CONSECUTIVO',
      'SUB CATEGORIA',
      'DESCRIPCION',
      'MARCA',
      'DIMENSIONES',
    ]);
    expect(wrapper.findAll('.file-cell').map((cell) => cell.text())).toEqual([
      '-H0004',
      'BASE TIPO BOOM',
      'Soporte roto',
      'DW',
      '',
    ]);
    expect(editor.text()).toContain('El archivo dice');
    expect(editor.text()).toContain('Lo que se importará · editable');
    // The bottom half is the item as it would be imported, prefilled from the item and the file:
    // the same form the inventory creates an item with, on the same attribute set.
    expect(input(wrapper, 'Descripción').props('modelValue')).toBe('Soporte roto');
    expect(select(wrapper, 'Subcategoría').props('modelValue')).toBe('BASE TIPO BOOM');
    expect(attributeRow(attributeRows(wrapper), 'Brand')).toBe('DW');
    expect(attributeRow(attributeRows(wrapper), 'Code')).toBe('-H0004');
    // Where the row would land, in the words of the tree.
    expect(editor.text()).toContain('BACKLINE › BASE TIPO BOOM');
  });

  it('prefills the description from the file cell, not from the code the API shows as label', async () => {
    const preview = previewFixture();
    const group = preview.sheets[0]!.groups[0]!;
    const red = group.items[3]!;
    mocks.preview.mockResolvedValue({
      ...preview,
      sheets: [
        {
          ...preview.sheets[0]!,
          groups: [
            {
              ...group,
              items: [
                ...group.items.slice(0, 3),
                { ...red, label: '-H0004', raw: { ...red.raw!, DESCRIPCION: '' } },
              ],
            },
          ],
        },
      ],
    });

    const wrapper = await mountPanel();
    await analyse(wrapper);
    await openRedRow(wrapper);

    // The real API fills `label` with the code for a "Sin descripción" row: editing that would
    // write a code as a name, so the description comes from the file's own cell.
    expect(input(wrapper, 'Descripción').props('modelValue')).toBe('');
    expect(button(wrapper, 'Guardar').props('disable')).toBe(true);
    // The code keeps its own row, prefilled from the item.
    expect(attributeRow(attributeRows(wrapper), 'Code')).toBe('-H0004');
  });

  it('shows no correction chip and opens nothing for a green row', async () => {
    const wrapper = await mountPanel();
    await analyse(wrapper);

    const green = itemRows(wrapper)[0]!;
    expect(green.find('.corrected-chip').exists()).toBe(false);
    expect(green.find('.edit-hint').exists()).toBe(false);
    expect(green.attributes('role')).toBeUndefined();

    await green.trigger('click');
    await flushPromises();
    expect(wrapper.findAll('.editor-card')).toHaveLength(0);
    expect(mocks.preview).toHaveBeenCalledTimes(1);
  });

  it('saves a description as a correction and shows the server answer, not a local guess', async () => {
    mocks.preview.mockReset();
    mocks.preview.mockResolvedValueOnce(previewFixture()).mockResolvedValueOnce(correctedPreview());

    const wrapper = await mountPanel();
    await analyse(wrapper);
    await openRedRow(wrapper);

    input(wrapper, 'Descripción').vm.$emit('update:modelValue', '  CONGA LP GALAXY 11 3/4  ');
    await flushPromises();
    await button(wrapper, 'Guardar').trigger('click');
    await flushPromises();

    // One correction, trimmed, carrying the sheet the row came from: row numbers repeat. The code,
    // the brand and the subcategory are the file's own, so they are **not** in the payload: a
    // correction is the difference from the file, never the whole form.
    expect(mocks.preview).toHaveBeenLastCalledWith(expect.any(File), [
      {
        source: 'BACKLINE',
        row: 15,
        label: 'CONGA LP GALAXY 11 3/4',
      },
    ]);
    const [, sent] = mocks.preview.mock.calls[1] as [File, ImportCorrection[]];
    expect(sent[0]).not.toHaveProperty('attributes');

    // The dialog is done and the tree is the answer: that row is green under its group now.
    expect(wrapper.find('.editor-card').exists()).toBe(false);
    const rows = itemRows(wrapper);
    expect(rows).toHaveLength(4);
    expect(rows[3]!.find('.item-status').text()).toBe('verde');
    expect(rows[3]!.text()).toContain('CONGA LP GALAXY 11 3/4');
    expect(rows[3]!.find('.corrected-chip').text()).toBe('Corregido');
    // The counters moved with it, because they are read off the server's tree.
    expect(wrapper.find('.sheet-row .count-green').text()).toBe('2');
    expect(wrapper.find('.sheet-row .count-red').text()).toBe('0');
  });

  it('accumulates corrections: the second preview carries both', async () => {
    mocks.preview.mockReset();
    mocks.preview
      .mockResolvedValueOnce(previewFixture())
      .mockResolvedValueOnce(correctedPreview())
      .mockResolvedValueOnce(correctedPreview());

    const wrapper = await mountPanel();
    await analyse(wrapper);

    await openRedRow(wrapper);
    input(wrapper, 'Descripción').vm.$emit('update:modelValue', 'CONGA LP GALAXY 11 3/4');
    await flushPromises();
    await button(wrapper, 'Guardar').trigger('click');
    await flushPromises();

    // The yellow one, still where the server put it.
    await itemRows(wrapper)[1]!.trigger('click');
    await flushPromises();
    input(wrapper, 'Descripción').vm.$emit('update:modelValue', 'Cuerpo de bombo 22"');
    await flushPromises();
    await button(wrapper, 'Guardar').trigger('click');
    await flushPromises();

    const [, sent] = mocks.preview.mock.calls[2] as [File, ImportCorrection[]];
    expect(sent.map((correction) => correction.row)).toEqual([13, 15]);
    expect(sent[0]).toMatchObject({ source: 'BACKLINE', row: 13, label: 'Cuerpo de bombo 22"' });
    expect(sent[1]).toMatchObject({
      source: 'BACKLINE',
      row: 15,
      label: 'CONGA LP GALAXY 11 3/4',
    });
  });

  it('undoes a correction with one button, and asks for the file back', async () => {
    mocks.preview.mockReset();
    mocks.preview
      .mockResolvedValueOnce(previewFixture())
      .mockResolvedValueOnce(correctedPreview())
      .mockResolvedValueOnce(previewFixture());

    const wrapper = await mountPanel();
    await analyse(wrapper);
    await openRedRow(wrapper);
    input(wrapper, 'Descripción').vm.$emit('update:modelValue', 'CONGA LP GALAXY 11 3/4');
    await flushPromises();
    await button(wrapper, 'Guardar').trigger('click');
    await flushPromises();

    expect(wrapper.findAll('.corrected-chip')).toHaveLength(1);
    await wrapper.find('.undo-correction').trigger('click');
    await flushPromises();

    // Back to the file: the payload is empty again and the mark is gone.
    expect(mocks.preview).toHaveBeenLastCalledWith(expect.any(File), []);
    expect(wrapper.findAll('.corrected-chip')).toHaveLength(0);
    expect(itemRows(wrapper)[3]!.find('.item-status').text()).toBe('rojo');
  });

  it('sends the corrections with the import, so the write is what the user fixed', async () => {
    mocks.preview.mockReset();
    mocks.preview.mockResolvedValueOnce(previewFixture()).mockResolvedValueOnce(correctedPreview());
    mocks.run.mockResolvedValue(outcomeFixture({ corrected: 1 }));

    const wrapper = await mountPanel();
    await analyse(wrapper);
    await openRedRow(wrapper);
    input(wrapper, 'Descripción').vm.$emit('update:modelValue', 'CONGA LP GALAXY 11 3/4');
    await flushPromises();
    await button(wrapper, 'Guardar').trigger('click');
    await flushPromises();

    await button(wrapper, 'Importar').trigger('click');
    await flushPromises();

    expect(mocks.run).toHaveBeenCalledWith(expect.any(File), {
      includeYellow: true,
      skipCodes: [],
      corrections: [
        {
          source: 'BACKLINE',
          row: 15,
          label: 'CONGA LP GALAXY 11 3/4',
        },
      ],
    });
    expect(wrapper.find('.result-skipped').text()).toContain('Correcciones aplicadas: 1');
  });

  it('refuses to save without a description, which the import could not take anyway', async () => {
    const wrapper = await mountPanel();
    await analyse(wrapper);
    await openRedRow(wrapper);

    input(wrapper, 'Descripción').vm.$emit('update:modelValue', '   ');
    await flushPromises();

    expect(button(wrapper, 'Guardar').props('disable')).toBe(true);
    await button(wrapper, 'Guardar').trigger('click');
    await flushPromises();
    // Only the analyse call so far: an empty description was never sent.
    expect(mocks.preview).toHaveBeenCalledTimes(1);
  });

  it('shows the corrections the server refused instead of dropping them silently', async () => {
    mocks.preview.mockReset();
    mocks.preview.mockResolvedValueOnce(previewFixture()).mockResolvedValueOnce({
      ...previewFixture(),
      correctionIssues: ['Fila 15: es ambigua; indique source.'],
    });

    const wrapper = await mountPanel();
    await analyse(wrapper);
    await openRedRow(wrapper);
    input(wrapper, 'Descripción').vm.$emit('update:modelValue', 'CONGA');
    await flushPromises();
    await button(wrapper, 'Guardar').trigger('click');
    await flushPromises();

    const banner = wrapper.find('.corrections-warning');
    expect(banner.exists()).toBe(true);
    expect(banner.text()).toContain('es ambigua; indique source');
  });

  it('offers the same attribute set the inventory does, and the name owns the description', async () => {
    const wrapper = await mountPanel();
    await analyse(wrapper);
    await openRedRow(wrapper);

    // Configuration's labels first, then the seven the importer derives — a code the file writes is
    // an attribute the form has to be able to reach. `Description` is absent because the name field
    // above the form already is it: two fields for one cell can only disagree.
    expect(attributeRows(wrapper).map((row) => row.code)).toEqual([
      'Brand',
      'Notes',
      'Category',
      'Code',
      'Reference',
      'Serial',
      'Dimensions',
    ]);
  });

  it('sends a cleared attribute as an empty string, which is how the API removes it', async () => {
    mocks.preview.mockReset();
    mocks.preview.mockResolvedValueOnce(previewFixture()).mockResolvedValueOnce(correctedPreview());

    const wrapper = await mountPanel();
    await analyse(wrapper);
    await openRedRow(wrapper);

    // The file says the brand is DW; the user empties it. Nothing else is touched.
    await setAttribute(wrapper, 'Brand', '');
    await button(wrapper, 'Guardar').trigger('click');
    await flushPromises();

    expect(mocks.preview).toHaveBeenLastCalledWith(expect.any(File), [
      {
        source: 'BACKLINE',
        row: 15,
        attributes: { Brand: '' },
      },
    ]);
  });

  it('sends a new attribute the file never had, and no untouched one beside it', async () => {
    mocks.preview.mockReset();
    mocks.preview.mockResolvedValueOnce(previewFixture()).mockResolvedValueOnce(correctedPreview());

    const wrapper = await mountPanel();
    await analyse(wrapper);
    await openRedRow(wrapper);

    await setAttributeRows(wrapper, [
      ...attributeRows(wrapper),
      { code: 'Estado', value: 'Bueno' },
    ]);
    await button(wrapper, 'Guardar').trigger('click');
    await flushPromises();

    // One key, the one that was typed: the rows prefilled from the file are the file, not edits.
    expect(mocks.preview).toHaveBeenLastCalledWith(expect.any(File), [
      {
        source: 'BACKLINE',
        row: 15,
        attributes: { Estado: 'Bueno' },
      },
    ]);
  });

  it('sends nothing at all when the user changed nothing', async () => {
    const wrapper = await mountPanel();
    await analyse(wrapper);
    await openRedRow(wrapper);

    await button(wrapper, 'Guardar').trigger('click');
    await flushPromises();

    // No correction, no round trip: only the analyse call so far and the dialog is closed. A
    // correction that carries the file unchanged would be counted as an edit nobody made.
    expect(mocks.preview).toHaveBeenCalledTimes(1);
    expect(wrapper.find('.editor-card').exists()).toBe(false);
  });

  it('opens maximized below sm, where the file row is wider than the phone', async () => {
    const wrapper = await mountPanel(true);
    await analyse(wrapper);

    expect(wrapper.findComponent({ name: 'QDialog' }).props('maximized')).toBe(true);

    await wrapper.findAll('.import-card')[3]!.trigger('click');
    await flushPromises();

    expect(wrapper.find('.editor-card').exists()).toBe(true);
    // The spreadsheet row keeps its columns and scrolls sideways instead of squeezing into 390px.
    expect(wrapper.find('.file-sheet-scroll').exists()).toBe(true);
    expect(wrapper.findAll('.file-header')).toHaveLength(5);
  });
});
