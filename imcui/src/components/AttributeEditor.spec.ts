import { defineComponent, h } from 'vue';
import { describe, expect, it } from 'vitest';
import { mount, type VueWrapper } from '@vue/test-utils';
import AttributeEditor from './AttributeEditor.vue';
import type { Attributes } from '@services/items';

// Quasar's components are replaced with stubs that keep the props visible: what matters here is
// which control a typed row gets, not how the control looks.
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
    options: { type: Array, default: () => [] },
    displayValue: { type: String, default: '' },
    placeholder: { type: String, default: '' },
  },
  emits: ['update:modelValue'],
  render() {
    return h('div', { class: 'q-select-stub' }, this.displayValue || String(this.modelValue ?? ''));
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

const stubs = { QInput, QSelect, QBtn, QIcon };

const labels = ['Brand', 'Type', 'BuyPrice', 'RentPrice', 'Description', 'Notes'];

const types: Record<string, string> = {
  Brand: 'Text',
  Type: 'Text',
  BuyPrice: 'Money',
  RentPrice: 'Money',
  Description: 'LongText',
  Notes: 'LongText',
};

function rowsFixture(extra: Attributes[] = []): Attributes[] {
  return [
    ...labels.map((label) => ({ label, value: '' })),
    ...extra,
  ];
}

function mountEditor(rows: Attributes[], typeOverrides: Record<string, string> = {}) {
  const holder: { wrapper?: VueWrapper } = {};
  const wrapper = mount(AttributeEditor, {
    props: {
      modelValue: rows,
      labels,
      lists: {},
      types: { ...types, ...typeOverrides },
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

/** The rows currently bound to the component (the model, not the rendered DOM). */
function currentRows(wrapper: VueWrapper): Attributes[] {
  return (wrapper.props() as { modelValue: Attributes[] }).modelValue;
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
    const wrapper = mountEditor(rowsFixture(), { Notes: 'Date' });
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
});
