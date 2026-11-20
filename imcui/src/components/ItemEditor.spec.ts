import { defineComponent, h, type PropType } from 'vue';
import { describe, expect, it } from 'vitest';
import { mount, type VueWrapper } from '@vue/test-utils';
import ItemEditor from './ItemEditor.vue';
import type { Attributes } from '@services/items';
import type { CatalogueEntry } from '@services/constants';

// Quasar is stubbed down to the props this form reads: which control a field gets, what it is
// labelled, whether it can be typed into, and what it offers. `AttributeEditor` stays real — the
// whole point of this component is that it hosts that one, and stubbing it would test nothing.
const QInput = defineComponent({
  name: 'QInput',
  props: {
    modelValue: { type: [String, Number], default: '' },
    label: { type: String, default: '' },
    hint: { type: String, default: undefined },
    rules: { type: Array as PropType<unknown[]>, default: () => [] },
    readonly: { type: Boolean, default: false },
    type: { type: String, default: 'text' },
    placeholder: { type: String, default: undefined },
    autogrow: { type: Boolean, default: false },
    inputClass: { type: String, default: '' },
    inputmode: { type: String, default: undefined },
    maxlength: { type: [String, Number], default: undefined },
    hideBottomSpace: { type: Boolean, default: false },
  },
  emits: ['update:modelValue', 'focus', 'blur'],
  setup(props, { emit }) {
    return () =>
      h('input', {
        class: 'q-input-stub',
        'data-label': props.label,
        value: props.modelValue,
        readonly: props.readonly,
        type: props.type,
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
    hint: { type: String, default: undefined },
    useInput: { type: Boolean, default: false },
    newValueMode: { type: String, default: undefined },
    hideSelected: { type: Boolean, default: false },
    fillInput: { type: Boolean, default: true },
    inputDebounce: { type: [Number, String], default: 500 },
    optionsDense: { type: Boolean, default: false },
    readonly: { type: Boolean, default: false },
    placeholder: { type: String, default: '' },
    loading: { type: Boolean, default: false },
  },
  emits: ['update:modelValue', 'filter', 'popup-hide'],
  setup(props) {
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
      ]);
  },
});

const QBtn = defineComponent({
  name: 'QBtn',
  props: {
    label: { type: String, default: '' },
    icon: { type: String, default: '' },
    disable: { type: Boolean, default: false },
  },
  emits: ['click'],
  setup(props, { emit }) {
    return () =>
      h(
        'button',
        { disabled: props.disable, onClick: (event: MouseEvent) => emit('click', event) },
        props.label,
      );
  },
});

const QIcon = defineComponent({ name: 'QIcon', render: () => h('i') });
const QItem = defineComponent({
  name: 'QItem',
  render() {
    return h('div', { class: 'q-item' }, this.$slots.default?.());
  },
});
const QItemSection = defineComponent({
  name: 'QItemSection',
  render() {
    return h('div', { class: 'q-item-section' }, this.$slots.default?.());
  },
});
const QSpinner = defineComponent({ name: 'QSpinner', render: () => h('span') });

/** Options are objects carrying a `label` or plain strings, which is what both callers pass. */
function optionLabel(option: unknown): string {
  if (typeof option === 'string') return option;
  if (option && typeof option === 'object' && 'label' in option) {
    const label = (option as { label?: unknown }).label;
    return typeof label === 'string' ? label : '';
  }
  return '';
}

const stubs = { QInput, QSelect, QBtn, QIcon, QItem, QItemSection, QSpinner };

/** The catalogue as Configuration sends it now: a code for the item, a label for the screen. */
const entries: CatalogueEntry[] = [
  { code: 'Brand', label: 'Marca' },
  { code: 'Type', label: 'Tipo' },
  { code: 'Notes', label: 'Notas' },
];

function rowsFixture(): Attributes[] {
  return [
    { code: 'Brand', value: 'DW' },
    { code: 'Type', value: 'Boom stand' },
    { code: 'Notes', value: '' },
  ];
}

interface MountOverrides {
  mode?: 'tree' | 'group';
  label?: string;
  parent?: string | number | null;
  parentOptions?: { label: string; value: string | number | null }[];
  readonly?: boolean;
  copy?: Record<string, string>;
  labelRule?: (value: string) => boolean | string;
}

function mountEditor(overrides: MountOverrides = {}): VueWrapper {
  return mount(ItemEditor, {
    props: {
      label: overrides.label ?? 'Base de brazo',
      parent: overrides.parent ?? null,
      attributes: rowsFixture(),
      attributeEntries: entries,
      mode: overrides.mode ?? 'tree',
      parentOptions: overrides.parentOptions ?? [
        { label: 'Sin padre', value: null },
        { label: 'BACKLINE', value: 7 },
      ],
      readonly: overrides.readonly ?? false,
      ...(overrides.copy ? { copy: overrides.copy } : {}),
      ...(overrides.labelRule ? { labelRule: overrides.labelRule } : {}),
    },
    global: { stubs },
  });
}

/** The name field, found by the label the form gave it. */
function nameInput(wrapper: VueWrapper, label = 'Nombre') {
  const found = wrapper
    .findAllComponents({ name: 'QInput' })
    .find((candidate) => candidate.props('label') === label);
  if (!found) throw new Error(`no input labelled ${label}`);
  return found;
}

/** The parent field: the select that carries a label (the attribute rows carry none). */
function parentSelect(wrapper: VueWrapper) {
  const found = wrapper
    .findAllComponents({ name: 'QSelect' })
    .find((candidate) => String(candidate.props('label') ?? '').length > 0);
  if (!found) throw new Error('no labelled select');
  return found;
}

describe('ItemEditor', () => {
  it('renders the name, the parent and the attribute rows it was given', () => {
    const wrapper = mountEditor();

    expect(nameInput(wrapper).props('modelValue')).toBe('Base de brazo');
    expect(parentSelect(wrapper).props('modelValue')).toBe(null);
    // The blank-tree sentinel is spelled out, so an empty field is not mistaken for a missing one.
    expect(parentSelect(wrapper).props('displayValue')).toBe('Sin padre');
    // Every configured label has its row, in the order Configuration gave them.
    expect(wrapper.findAll('.attribute-row')).toHaveLength(3);
    expect(wrapper.findAll('.attribute-row')[0]!.text()).toContain('Marca');
  });

  it('renders the actions the host puts in the details header', () => {
    const wrapper = mount(ItemEditor, {
      props: {
        label: 'Base',
        parent: null as string | number | null,
        attributes: rowsFixture(),
        attributeEntries: entries,
      },
      slots: { 'details-actions': '<button class="host-action">Añadir ítem hijo</button>' },
      global: { stubs },
    });

    // The inventory keeps its "Añadir ítem hijo" button there; the dialog puts nothing in it.
    expect(wrapper.find('.host-action').exists()).toBe(true);
    expect(wrapper.findAll('.imc-section')[0]!.text()).toContain('Añadir ítem hijo');
  });

  it('passes the choice lists and the types it is given to the attribute editor', () => {
    const wrapper = mount(ItemEditor, {
      props: {
        label: 'Base',
        parent: null as string | number | null,
        attributes: rowsFixture(),
        attributeEntries: entries,
        attributeLists: {
          Brand: [
            { code: 'DW', label: 'DW' },
            { code: 'Pearl', label: 'Pearl' },
            { code: 'Yamaha', label: 'Yamaha' },
          ],
        },
        attributeTypes: { Notes: 'LongText' },
      },
      global: { stubs },
    });

    // A label with a list gets the searchable select; `Notes` is prose, so it gets the textarea.
    const notes = wrapper.findAll('.attribute-row')[2]!;
    expect(notes.findAllComponents({ name: 'QInput' }).at(-1)!.props('type')).toBe('textarea');
    const brand = wrapper.findAll('.attribute-row')[0]!;
    expect(brand.findAllComponents({ name: 'QSelect' }).length).toBeGreaterThan(1);
  });

  it('hides the inputs and the row buttons when the session cannot write', () => {
    const wrapper = mountEditor({ readonly: true });

    expect(nameInput(wrapper).props('readonly')).toBe(true);
    expect(parentSelect(wrapper).props('readonly')).toBe(true);
    // The value of every row is read-only too, and there is no way to add or remove one.
    for (const row of wrapper.findAll('.attribute-row')) {
      for (const input of row.findAllComponents({ name: 'QInput' })) {
        expect(input.props('readonly')).toBe(true);
      }
    }
    expect(wrapper.find('.attribute-actions').exists()).toBe(false);
    expect(wrapper.find('.attribute-remove').exists()).toBe(false);
  });

  it('offers the row buttons when the session can write', () => {
    const wrapper = mountEditor();

    expect(nameInput(wrapper).props('readonly')).toBe(false);
    expect(wrapper.find('.attribute-actions').exists()).toBe(true);
    expect(wrapper.findAll('.attribute-remove').length).toBeGreaterThan(0);
  });

  it('emits the name and the parent the user picks', async () => {
    const wrapper = mountEditor();

    await nameInput(wrapper).vm.$emit('update:modelValue', 'Base de brazo DW 9000');
    await parentSelect(wrapper).vm.$emit('update:modelValue', 7);

    expect(wrapper.emitted('update:label')?.at(-1)).toEqual(['Base de brazo DW 9000']);
    expect(wrapper.emitted('update:parent')?.at(-1)).toEqual([7]);
  });

  it('gives the name the validation rule of the screen that hosts it', () => {
    const required = (value: string) => value.trim().length > 0 || 'El nombre es obligatorio';
    const wrapper = mountEditor({ labelRule: required });

    expect(nameInput(wrapper).props('rules')).toEqual([required]);
  });

  it('keeps the parent field a closed list in the tree, where it is an item id', () => {
    const wrapper = mountEditor({ mode: 'tree' });

    // Typing a parent that is not an item must be impossible: the tree is the list.
    expect(parentSelect(wrapper).props('useInput')).toBe(false);
    expect(parentSelect(wrapper).props('newValueMode')).toBe(undefined);
    expect(
      parentSelect(wrapper)
        .findAll('.q-select-stub__option')
        .map((o) => o.text()),
    ).toEqual(['Sin padre', 'BACKLINE']);
  });

  it('lets the group field name a new group, where the parent is a subcategory', () => {
    const wrapper = mountEditor({
      mode: 'group',
      parent: 'BASE TIPO BOOM',
      parentOptions: [
        { label: 'BASE TIPO BOOM', value: 'BASE TIPO BOOM' },
        { label: 'CyMBALS', value: 'CyMBALS' },
      ],
    });

    // A subcategory the configuration does not have yet is named here, not in another screen.
    expect(parentSelect(wrapper).props('useInput')).toBe(true);
    expect(parentSelect(wrapper).props('newValueMode')).toBe('add-unique');
    expect(parentSelect(wrapper).props('modelValue')).toBe('BASE TIPO BOOM');
    expect(parentSelect(wrapper).props('displayValue')).toBe('');
  });

  it('words the fields the way the host screen asks, and keeps the defaults untouched', () => {
    const wrapper = mountEditor({
      copy: {
        details: 'El ítem',
        name: 'Descripción',
        parent: 'Subcategoría',
        parentEmpty: 'Sin grupo',
      },
      parent: null,
      parentOptions: [{ label: 'Sin grupo', value: null }],
    });

    expect(nameInput(wrapper, 'Descripción').exists()).toBe(true);
    expect(parentSelect(wrapper).props('label')).toBe('Subcategoría');
    // The sections keep their headings, so the dialog and the page read the same.
    expect(wrapper.text()).toContain('El ítem');
    expect(wrapper.text()).toContain('Atributos');
  });
});
