import { defineComponent, h, type PropType } from 'vue';
import { describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import AttributeEditor from './AttributeEditor.vue';
import type { Attributes } from '@services/items';

// Quasar's components are replaced with stubs that keep the props visible: what matters here is
// which control a typed row gets, not how the control looks. The select stub renders the slots the
// component fills (no-option, after-options), because the "add a value" row lives in them.
const QInput = defineComponent({
  name: 'QInput',
  props: {
    modelValue: { type: [String, Number], default: '' },
    type: { type: String, default: 'text' },
    inputmode: { type: String, default: undefined },
    placeholder: { type: String, default: '' },
    autogrow: { type: Boolean, default: false },
    inputClass: { type: String, default: '' },
    maxlength: { type: [String, Number], default: undefined },
    hideBottomSpace: { type: Boolean, default: false },
  },
  emits: ['update:modelValue', 'focus', 'blur'],
  render() {
    return h('input', { value: this.modelValue, type: this.type, placeholder: this.placeholder });
  },
});

const QSelect = defineComponent({
  name: 'QSelect',
  props: {
    modelValue: { type: [String, Number], default: '' },
    options: { type: Array as PropType<unknown[]>, default: () => [] },
    displayValue: { type: String, default: '' },
    placeholder: { type: String, default: '' },
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
  render() {
    const options = this.options;
    const list = h(
      'ul',
      { class: 'q-select-stub__options' },
      options.map((option) => h('li', { class: 'q-select-stub__option' }, String(option))),
    );
    // Quasar shows the no-option slot only when nothing matches, and after-options only when the
    // list has entries; mirroring that keeps the assertions honest.
    const slots = options.length
      ? this.$slots['after-options']?.()
      : this.$slots['no-option']?.({ inputValue: '' });

    return h('div', { class: 'q-select-stub' }, [
      h(
        'span',
        { class: 'q-select-stub__display' },
        this.displayValue || String(this.modelValue ?? ''),
      ),
      list,
      slots ?? null,
    ]);
  },
});

const QBtn = defineComponent({
  name: 'QBtn',
  props: {
    label: { type: String, default: '' },
    icon: { type: String, default: '' },
    disable: { type: Boolean, default: false },
    type: { type: String, default: 'button' },
  },
  emits: ['click'],
  render() {
    return h('button', { disabled: this.disable, onClick: (e: MouseEvent) => this.$emit('click', e) },
      this.label);
  },
});

const QIcon = defineComponent({ name: 'QIcon', render: () => h('i') });
const QItem = defineComponent({
  name: 'QItem',
  props: { clickable: { type: Boolean, default: false } },
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
const QSpinner = defineComponent({
  name: 'QSpinner',
  render() {
    return h('span', { class: 'q-spinner' });
  },
});

const stubs = { QInput, QSelect, QBtn, QIcon, QItem, QItemSection, QSpinner };

const labels = ['Brand', 'Type', 'BuyPrice', 'RentPrice', 'Description', 'Notes'];

const types: Record<string, string> = {
  Brand: 'Text',
  Type: 'Text',
  BuyPrice: 'Money',
  RentPrice: 'Money',
  Description: 'LongText',
  Notes: 'LongText',
};

const brandValues = ['Bosch', 'DeWalt', 'Makita'];

function rowsFixture(extra: Attributes[] = []): Attributes[] {
  return [...labels.map((label) => ({ label, value: '' })), ...extra];
}

interface MountOverrides {
  types?: Record<string, string>;
  lists?: Record<string, string[]>;
  createValue?: (label: string, value: string) => Promise<boolean>;
  readonly?: boolean;
}

function mountEditor(rows: Attributes[], overrides: MountOverrides = {}) {
  const holder: { wrapper?: VueWrapper } = {};
  const wrapper = mount(AttributeEditor, {
    props: {
      modelValue: rows,
      labels,
      lists: overrides.lists ?? {},
      types: { ...types, ...(overrides.types ?? {}) },
      readonly: overrides.readonly ?? false,
      ...(overrides.createValue ? { createValue: overrides.createValue } : {}),
      'onUpdate:modelValue': (value: Attributes[]) =>
        holder.wrapper?.setProps({ modelValue: value }),
    },
    global: { stubs },
  });
  holder.wrapper = wrapper;
  return wrapper;
}

/** The value control of a row: the last input inside it. */
function valueInput(wrapper: VueWrapper, rowIndex: number) {
  const row = wrapper.findAll('.attribute-row')[rowIndex]!;
  const inputs = row.findAllComponents({ name: 'QInput' });
  return inputs[inputs.length - 1]!;
}

/** The value select of a row: the last select inside it (the first one picks the name). */
function valueSelect(wrapper: VueWrapper, rowIndex: number) {
  const row = wrapper.findAll('.attribute-row')[rowIndex]!;
  const selects = row.findAllComponents({ name: 'QSelect' });
  return selects[selects.length - 1]!;
}

/** The rows currently bound to the component (the model, not the rendered DOM). */
function currentRows(wrapper: VueWrapper): Attributes[] {
  return (wrapper.props() as { modelValue: Attributes[] }).modelValue;
}

/** Types into a row's value list the way Quasar does when the user types. */
async function typeIntoValueList(wrapper: VueWrapper, rowIndex: number, text: string) {
  const select = valueSelect(wrapper, rowIndex);
  // Quasar hands the filter listener an `update` callback that runs the work and refreshes the
  // menu; a select that filters its own options has to call it or the field never filters.
  const update = vi.fn((callback?: () => void) => callback?.());
  await select.vm.$emit('filter', text, update);
  await wrapper.vm.$nextTick();
  expect(update).toHaveBeenCalled();
  return valueSelect(wrapper, rowIndex);
}

describe('AttributeEditor', () => {
  it('gives money a decimal keypad and right alignment', () => {
    const wrapper = mountEditor(rowsFixture());
    // Brand, Type, BuyPrice …
    const input = valueInput(wrapper, 2);

    expect(input.props('inputmode')).toBe('decimal');
    expect(input.props('inputClass')).toContain('text-right');
  });

  it('gives prose a textarea that spans the row', () => {
    const wrapper = mountEditor(rowsFixture());
    const input = valueInput(wrapper, 4); // Description

    expect(input.props('type')).toBe('textarea');
    expect(input.props('autogrow')).toBe(true);
    expect(wrapper.findAll('.attribute-row')[4]!.classes()).toContain('attribute-row--wide');
  });

  it('gives a date the calendar control', () => {
    const wrapper = mountEditor(rowsFixture(), { types: { Notes: 'Date' } });
    const input = valueInput(wrapper, 5); // Notes

    expect(input.props('type')).toBe('date');
  });

  it('keeps an ordinary text value exactly as typed', async () => {
    const wrapper = mountEditor(rowsFixture());
    const input = valueInput(wrapper, 0); // Brand

    await input.vm.$emit('update:modelValue', 'DeWalt 12"');

    expect(currentRows(wrapper)[0]).toEqual({ label: 'Brand', value: 'DeWalt 12"' });
  });

  it('scrubs a money value down to digits and one separator', async () => {
    const wrapper = mountEditor(rowsFixture());
    const input = valueInput(wrapper, 2); // BuyPrice

    await input.vm.$emit('update:modelValue', '12abc000');

    expect(currentRows(wrapper)[2]).toEqual({ label: 'BuyPrice', value: '12000' });
  });

  it('shows a row for a key the catalogue does not have, with an editable name', () => {
    const wrapper = mountEditor(rowsFixture([{ label: 'Serial', value: 'SN-0042' }]));
    const custom = wrapper.findAll('.attribute-row').at(-1)!;

    expect(custom.findComponent({ name: 'QInput' }).props('placeholder')).toBe('Attribute name');
    expect(custom.classes()).toContain('attribute-row');
  });

  it('adds a custom row with an empty key when asked', async () => {
    const wrapper = mountEditor(rowsFixture());
    const addCustom = wrapper.findAllComponents({ name: 'QBtn' })
      .find((button) => button.props('label') === 'Add custom')!;

    await addCustom.trigger('click');

    expect(currentRows(wrapper).at(-1)).toEqual({ label: '', value: '' });
  });

  it('filters the rows by name or value once there are more than five', async () => {
    const wrapper = mountEditor(rowsFixture());
    expect(wrapper.findAll('.attribute-row')).toHaveLength(6);

    const filter = wrapper.findAllComponents({ name: 'QInput' })
      .find((input) => input.props('placeholder') === 'Filter attributes')!;
    expect(filter).toBeTruthy();

    await filter.vm.$emit('update:modelValue', 'price');
    expect(wrapper.findAll('.attribute-row')).toHaveLength(2);

    await filter.vm.$emit('update:modelValue', 'nothing here');
    expect(wrapper.findAll('.attribute-row')).toHaveLength(0);
  });

  it('lets a phone filter a value list instead of only showing it', () => {
    const wrapper = mountEditor(rowsFixture(), { lists: { Brand: brandValues } });
    const select = valueSelect(wrapper, 0);

    expect(select.props('useInput')).toBe(true);
    expect(select.props('inputDebounce')).toBe(0);
    expect(select.props('hideSelected')).toBe(true);
    // Not off: with hide-selected and fill-input off Quasar renders neither the value nor its
    // label, so the chosen value would be invisible in the row.
    expect(select.props('fillInput')).toBe(true);
    expect(select.props('optionsDense')).toBe(true);
    expect(select.props('readonly')).toBe(false);
    expect(select.attributes('autocomplete')).toBe('off');
    expect(select.attributes('inputmode')).toBe('search');
  });

  it('narrows a value list as the user types and restores it when the dropdown closes', async () => {
    const wrapper = mountEditor(rowsFixture(), { lists: { Brand: brandValues } });

    expect(valueSelect(wrapper, 0).props('options')).toEqual(brandValues);

    expect((await typeIntoValueList(wrapper, 0, 'dew')).props('options')).toEqual(['DeWalt']);

    await valueSelect(wrapper, 0).vm.$emit('popup-hide');
    await wrapper.vm.$nextTick();
    expect(valueSelect(wrapper, 0).props('options')).toEqual(brandValues);
  });

  it('offers the typed string when it matches no option', async () => {
    const wrapper = mountEditor(rowsFixture(), {
      lists: { Brand: brandValues },
      createValue: vi.fn().mockResolvedValue(true),
    });

    const select = await typeIntoValueList(wrapper, 0, 'Acme');

    expect(select.props('options')).toEqual([]);
    const addRow = wrapper.find('.attribute-add-option');
    expect(addRow.exists()).toBe(true);
    expect(addRow.text()).toContain('Añadir «Acme» a «Brand»');
  });

  it('adds the value only once the write resolves, with the field busy meanwhile', async () => {
    let resolveWrite: (created: boolean) => void = () => undefined;
    const createValue = vi.fn(
      () => new Promise<boolean>((resolve) => (resolveWrite = resolve)),
    );
    const wrapper = mountEditor(rowsFixture(), {
      lists: { Brand: brandValues },
      createValue,
    });

    await typeIntoValueList(wrapper, 0, '  Acme  ');
    await wrapper.find('.attribute-add-option').trigger('click');

    expect(createValue).toHaveBeenCalledWith('Brand', 'Acme');
    expect(currentRows(wrapper)[0]!.value).toBe('');
    expect(valueSelect(wrapper, 0).props('loading')).toBe(true);

    // A second tap while the first write is in flight must not write twice.
    await wrapper.find('.attribute-add-option').trigger('click');
    expect(createValue).toHaveBeenCalledTimes(1);

    resolveWrite(true);
    await flushPromises();

    expect(currentRows(wrapper)[0]).toEqual({ label: 'Brand', value: 'Acme' });
    expect(valueSelect(wrapper, 0).props('loading')).toBe(false);
  });

  it('keeps the previous value and shows the error when the write fails', async () => {
    const rows = rowsFixture();
    rows[0]!.value = 'Bosch';
    const wrapper = mountEditor(rows, {
      lists: { Brand: brandValues },
      createValue: vi.fn().mockResolvedValue(false),
    });

    await typeIntoValueList(wrapper, 0, 'Acme');
    await wrapper.find('.attribute-add-option').trigger('click');
    await flushPromises();

    expect(currentRows(wrapper)[0]).toEqual({ label: 'Brand', value: 'Bosch' });
    const error = wrapper.find('.attribute-error');
    expect(error.exists()).toBe(true);
    expect(error.text()).toContain('Acme');
    // The typed string is dropped with the failure: the list is whole again, ready for a retry.
    expect(valueSelect(wrapper, 0).props('options')).toEqual(brandValues);
  });

  it('offers no way to add a value when the page passes no createValue', async () => {
    const wrapper = mountEditor(rowsFixture(), { lists: { Brand: brandValues } });

    await typeIntoValueList(wrapper, 0, 'Acme');

    expect(wrapper.find('.attribute-add-option').exists()).toBe(false);
  });

  it('never offers an existing option as a bare string to add', async () => {
    const wrapper = mountEditor(rowsFixture(), {
      lists: { Brand: brandValues },
      createValue: vi.fn().mockResolvedValue(true),
    });

    // Same value in another case: the list already has it, so there is nothing to add.
    const select = await typeIntoValueList(wrapper, 0, 'dewalt');

    expect(select.props('options')).toEqual(['DeWalt']);
    expect(wrapper.find('.attribute-add-option').exists()).toBe(false);
  });
});
