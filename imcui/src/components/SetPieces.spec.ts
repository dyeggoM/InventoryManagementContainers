import { defineComponent, h, type PropType } from 'vue';
import { describe, expect, it } from 'vitest';
import { mount, type VueWrapper } from '@vue/test-utils';
import SetPieces from './SetPieces.vue';
import type { Item } from '@services/items';

// Quasar is stubbed down to what this section reads: which controls exist, what they are labelled,
// whether they can be pressed, and what the dialog shows once it is open. `services/search.ts` and
// `prettyLabel` stay real — the dialog is meant to search exactly like the tree.
const QBtn = defineComponent({
  name: 'QBtn',
  props: {
    label: { type: String, default: '' },
    icon: { type: String, default: '' },
    ariaLabel: { type: String, default: '' },
    disable: { type: Boolean, default: false },
  },
  emits: ['click'],
  setup(props, { emit }) {
    return () =>
      h(
        'button',
        {
          'data-icon': props.icon,
          'data-label': props.label,
          'aria-label': props.ariaLabel,
          disabled: props.disable,
          onClick: (event: MouseEvent) => emit('click', event),
        },
        props.label,
      );
  },
});

const QDialog = defineComponent({
  name: 'QDialog',
  props: {
    modelValue: { type: Boolean, default: false },
    maximized: { type: Boolean, default: false },
  },
  setup(props, { slots }) {
    return () =>
      h(
        'div',
        { class: 'q-dialog-stub', 'data-open': String(props.modelValue) },
        props.modelValue ? slots.default?.() : [],
      );
  },
});

const QInput = defineComponent({
  name: 'QInput',
  props: {
    modelValue: { type: String, default: '' },
    label: { type: String, default: '' },
    placeholder: { type: String, default: '' },
  },
  emits: ['update:modelValue'],
  setup(props, { emit, slots }) {
    return () =>
      h('div', { class: 'q-input-stub' }, [
        h('input', {
          'data-placeholder': props.placeholder,
          value: props.modelValue,
          onInput: (event: Event) =>
            emit('update:modelValue', (event.target as HTMLInputElement).value),
        }),
        slots.prepend?.(),
      ]);
  },
});

const QCheckbox = defineComponent({
  name: 'QCheckbox',
  props: {
    modelValue: { type: Array as PropType<number[]>, default: () => [] },
    val: { type: Number, default: 0 },
  },
  emits: ['update:modelValue'],
  setup(props, { emit }) {
    return () =>
      h('input', {
        class: 'q-checkbox-stub',
        type: 'checkbox',
        'data-val': String(props.val),
        checked: props.modelValue.includes(props.val),
        onChange: () =>
          emit(
            'update:modelValue',
            props.modelValue.includes(props.val)
              ? props.modelValue.filter((id) => id !== props.val)
              : [...props.modelValue, props.val],
          ),
      });
  },
});

/** Slots only: a card, a section, a toolbar add nothing this spec reads. */
function passthrough(name: string) {
  return defineComponent({
    name,
    setup(_props, { slots }) {
      return () =>
        h('div', { class: `${name}-stub` }, [
          ...(slots.default?.() ?? []),
          ...(slots.side ? [] : []),
        ]);
    },
  });
}

const QIcon = defineComponent({
  name: 'QIcon',
  props: { name: { type: String, default: '' } },
  setup(props) {
    return () => h('i', { class: 'q-icon-stub', 'data-icon': props.name });
  },
});

const QItem = passthrough('QItem');
const QItemSection = defineComponent({
  name: 'QItemSection',
  setup(_props, { slots }) {
    return () => h('div', { class: 'q-item-section-stub' }, slots.default?.());
  },
});

const QItemLabel = defineComponent({
  name: 'QItemLabel',
  setup(_props, { slots }) {
    return () => h('span', { class: 'q-item-label-stub' }, slots.default?.());
  },
});

const stubs = {
  QBtn,
  QIcon,
  QDialog,
  QInput,
  QCheckbox,
  QCard: passthrough('QCard'),
  QCardSection: passthrough('QCardSection'),
  QToolbar: passthrough('QToolbar'),
  QToolbarTitle: passthrough('QToolbarTitle'),
  QItem,
  QItemSection,
  QItemLabel,
  QSpace: defineComponent({ name: 'QSpace', render: () => h('span', { class: 'q-space-stub' }) }),
  QTooltip: passthrough('QTooltip'),
};

function item(id: number, label: string, attributes?: string): Item {
  return { id, label, icon: '', children: [], attributes };
}

const pieces = [
  item(11, 'Boom stand', '{"Brand":"DW","Code":"-H0001"}'),
  item(12, 'Clamp', '{"Brand":"Pearl"}'),
];

const candidates = [
  item(21, 'Base de brazo', '{"Brand":"Yamaha","Code":"-H0004"}'),
  item(22, 'Strap'),
  item(23, 'Snare drum', '{"Brand":"Pearl"}'),
];

interface MountOverrides {
  pieces?: Item[];
  candidates?: Item[];
  readonly?: boolean;
  busy?: boolean;
}

function mountSection(overrides: MountOverrides = {}): VueWrapper {
  return mount(SetPieces, {
    props: {
      pieces: overrides.pieces ?? pieces,
      candidates: overrides.candidates ?? candidates,
      readonly: overrides.readonly ?? false,
      busy: overrides.busy ?? false,
    },
    global: {
      stubs,
      mocks: { $q: { screen: { lt: { sm: false } } } },
    },
  });
}

function button(wrapper: VueWrapper, label: string) {
  const found = wrapper
    .findAllComponents({ name: 'QBtn' })
    .find((candidate) => candidate.props('label') === label);
  if (!found) throw new Error(`no button labelled ${label}`);
  return found;
}

/** The add button: its label is the only one the section shows while the dialog is closed. */
function addButton(wrapper: VueWrapper) {
  return button(wrapper, 'Añadir piezas');
}

async function openPicker(wrapper: VueWrapper): Promise<void> {
  await addButton(wrapper).trigger('click');
}

function searchInput(wrapper: VueWrapper) {
  const found = wrapper.findComponent({ name: 'QInput' });
  if (!found.exists()) throw new Error('the dialog has no search box');
  return found;
}

function rows(wrapper: VueWrapper) {
  return wrapper.findAllComponents({ name: 'QItem' });
}

function rowLabels(wrapper: VueWrapper): string[] {
  return rows(wrapper).map((row) => row.text());
}

describe('SetPieces', () => {
  it('lists the pieces with the brand and the code the inventory already carries', () => {
    const wrapper = mountSection();

    expect(wrapper.findAll('.set-pieces__row')).toHaveLength(2);
    expect(wrapper.text()).toContain('Boom stand');
    expect(wrapper.text()).toContain('DW · -H0001');
    // A piece with no code shows the brand alone, not a dangling separator.
    expect(wrapper.text()).toContain('Pearl');
    expect(wrapper.text()).not.toContain('Pearl ·');
  });

  it('says a set has no pieces yet instead of showing an empty list', () => {
    const wrapper = mountSection({ pieces: [] });

    expect(wrapper.find('.set-pieces__row').exists()).toBe(false);
    expect(wrapper.text()).toContain('Todavía no hay piezas');
  });

  it('offers the add and remove actions to a session that may write', () => {
    const wrapper = mountSection();

    expect(addButton(wrapper).exists()).toBe(true);
    expect(wrapper.findAll('button[aria-label^="Quitar "]')).toHaveLength(2);
  });

  it('hides both actions for a lector, who may still read the pieces', () => {
    const wrapper = mountSection({ readonly: true });

    expect(wrapper.findAllComponents({ name: 'QBtn' })).toHaveLength(0);
    expect(wrapper.text()).toContain('Boom stand');
    // Nothing to press, and the picker is never opened: there is no way to add pieces at all.
    expect(wrapper.find('.q-dialog-stub').attributes('data-open')).toBe('false');
  });

  it('disables the two writes while a request is in flight', () => {
    const wrapper = mountSection({ busy: true });

    expect(addButton(wrapper).props('disable')).toBe(true);
    for (const remove of wrapper.findAll('button[aria-label^="Quitar "]')) {
      expect(remove.attributes('disabled')).toBeDefined();
    }
  });

  it('detaches a piece through the event the page owns, never deleting it', async () => {
    const wrapper = mountSection();

    await wrapper.find('button[aria-label="Quitar Clamp del conjunto"]').trigger('click');

    expect(wrapper.emitted('remove')).toEqual([[12]]);
  });

  it('lists the candidates in the dialog, with no row for the pieces already attached', async () => {
    const wrapper = mountSection();
    await openPicker(wrapper);

    expect(rowLabels(wrapper)).toEqual([
      'Base de brazoYamaha · -H0004',
      'Strap',
      'Snare drumPearl',
    ]);
    // The pieces of this set are not offered again: attaching one would move it out of the set.
    expect(wrapper.find('.q-dialog-stub').text()).not.toContain('Boom stand');
  });

  it("searches the candidates with the tree's own box, by name and by attribute", async () => {
    const wrapper = mountSection();
    await openPicker(wrapper);

    await searchInput(wrapper).vm.$emit('update:modelValue', 'base');
    expect(rowLabels(wrapper)).toEqual(['Base de brazoYamaha · -H0004']);

    await searchInput(wrapper).vm.$emit('update:modelValue', 'pearl');
    // One match by brand, and the row says which attribute matched, as the tree does.
    expect(rowLabels(wrapper)).toEqual(['Snare drumBrand · Pearl']);
    expect(wrapper.text()).toContain('1 coincidencia');

    await searchInput(wrapper).vm.$emit('update:modelValue', 'zzz');
    expect(wrapper.text()).toContain('Nada coincide');
    expect(rows(wrapper)).toHaveLength(0);
  });

  it('ticks several pieces and confirms them in one event', async () => {
    const wrapper = mountSection();
    await openPicker(wrapper);

    expect(button(wrapper, 'Añadir 0 piezas').props('disable')).toBe(true);

    await wrapper.find('input[data-val="21"]').trigger('change');
    await wrapper.find('input[data-val="23"]').trigger('change');

    expect(wrapper.text()).toContain('2 seleccionadas');
    const confirm = button(wrapper, 'Añadir 2 piezas');
    expect(confirm.props('disable')).toBe(false);

    await confirm.trigger('click');

    // One call for the selection, then the dialog closes and forgets the ticks.
    expect(wrapper.emitted('add')).toEqual([[[21, 23]]]);
    expect(wrapper.find('.q-dialog-stub').attributes('data-open')).toBe('false');
  });

  it('words the confirm for a single piece', async () => {
    const wrapper = mountSection();
    await openPicker(wrapper);

    await wrapper.find('input[data-val="22"]').trigger('change');

    expect(button(wrapper, 'Añadir 1 pieza').exists()).toBe(true);
  });

  it('says so when every item already belongs to a set', async () => {
    const wrapper = mountSection({ candidates: [] });
    await openPicker(wrapper);

    expect(rows(wrapper)).toHaveLength(0);
    expect(wrapper.text()).toContain('todos los ítems pertenecen ya a un conjunto');
  });

  it('starts every opening from a clean search and an empty selection', async () => {
    const wrapper = mountSection();
    await openPicker(wrapper);
    await searchInput(wrapper).vm.$emit('update:modelValue', 'pearl');
    await wrapper.find('input[data-val="23"]').trigger('change');

    // Cancel, then open again: the previous question must not come back answered.
    await button(wrapper, 'Cancelar').trigger('click');
    await openPicker(wrapper);

    expect(searchInput(wrapper).props('modelValue')).toBe('');
    expect(rowLabels(wrapper)).toHaveLength(3);
    expect(wrapper.text()).toContain('0 seleccionadas');
  });
});
