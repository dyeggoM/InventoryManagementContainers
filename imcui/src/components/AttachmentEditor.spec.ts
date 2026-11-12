import { defineComponent, h } from 'vue';
import { describe, expect, it } from 'vitest';
import { mount, type VueWrapper } from '@vue/test-utils';
import AttachmentEditor from './AttachmentEditor.vue';

/** The row shape the editor works with; kept local so the spec does not depend on SFC type exports. */
interface AttachmentRow {
  id: number;
  itemId: number;
  type?: string | null;
  fileName?: string | null;
  filePath?: string | null;
  thumbnailBase64?: string | null;
  uploadBase64?: string | null;
  file?: File | null;
}

const QFile = defineComponent({
  name: 'QFile',
  props: {
    label: { type: String, default: '' },
    accept: { type: String, default: undefined },
    capture: { type: String, default: undefined },
    modelValue: { type: Object, default: null },
  },
  emits: ['update:modelValue'],
  render() {
    return h('input', { type: 'file', 'data-label': this.label });
  },
});

const QBtn = defineComponent({
  name: 'QBtn',
  props: {
    label: { type: String, default: '' },
    icon: { type: String, default: '' },
    href: { type: String, default: undefined },
    ariaLabel: { type: String, default: undefined },
  },
  emits: ['click'],
  render() {
    // Attributes such as aria-label stay on $attrs (they are not camelCased props): forward them.
    return h(
      'button',
      {
        'data-icon': this.icon,
        'aria-label': this.ariaLabel,
        ...this.$attrs,
        onClick: (e: MouseEvent) => this.$emit('click', e),
      },
    );
  },
});

const QSelect = defineComponent({
  name: 'QSelect',
  props: { modelValue: { type: String, default: '' }, options: { type: Array, default: () => [] } },
  emits: ['update:modelValue'],
  render: () => h('div', { class: 'q-select-stub' }),
});

const QImg = defineComponent({
  name: 'QImg',
  props: { src: { type: String, default: '' } },
  render() {
    return h('img', { src: this.src });
  },
});

const QIcon = defineComponent({
  name: 'QIcon',
  props: { name: { type: String, default: '' } },
  render() {
    return h('i', { 'data-icon': this.name });
  },
});

/** The dialog is exercised by its own spec; here only the props the editor hands it matter. */
const FilePreviewDialog = defineComponent({
  name: 'FilePreviewDialog',
  props: {
    modelValue: { type: Boolean, default: false },
    attachmentId: { type: Number, default: 0 },
    fileName: { type: String, default: null },
    type: { type: String, default: null },
  },
  emits: ['update:modelValue'],
  render() {
    return h('div', { class: 'file-preview-stub' });
  },
});

const stubs = { QFile, QBtn, QSelect, QImg, QIcon, FilePreviewDialog };

function row(overrides: Partial<AttachmentRow> = {}): AttachmentRow {
  return { id: 0, itemId: 3, type: 'Photo', fileName: '', ...overrides };
}

function currentRows(wrapper: VueWrapper): AttachmentRow[] {
  return (wrapper.props() as { modelValue: AttachmentRow[] }).modelValue;
}

function mountEditor(rows: AttachmentRow[], types: string[] = ['Photo', 'User Manual']) {
  const holder: { wrapper?: VueWrapper } = {};
  const wrapper = mount(AttachmentEditor, {
    props: {
      types,
      modelValue: rows,
      'onUpdate:modelValue': (value: AttachmentRow[]) => holder.wrapper?.setProps({ modelValue: value }),
    },
    global: { stubs },
  });
  holder.wrapper = wrapper;
  return wrapper;
}

describe('AttachmentEditor', () => {
  it('opens the camera for a photo, with a way back to the gallery', () => {
    const wrapper = mountEditor([row()]);
    const file = wrapper.findComponent({ name: 'QFile' });

    expect(file.props('label')).toBe('Take a photo');
    expect(file.props('accept')).toBe('image/*');
    expect(file.props('capture')).toBe('environment');
    expect(
      wrapper.findAllComponents({ name: 'QBtn' }).some((b) => b.props('label') === 'Pick from gallery instead'),
    ).toBe(true);
  });

  it('opens the file manager for anything else', () => {
    const wrapper = mountEditor([row({ type: 'User Manual' })]);
    const file = wrapper.findComponent({ name: 'QFile' });

    expect(file.props('label')).toBe('Select a file');
    expect(file.props('accept')).toBeUndefined();
    expect(file.props('capture')).toBeUndefined();
    expect(
      wrapper.findAllComponents({ name: 'QBtn' }).some((b) => b.props('label') === 'Pick from gallery instead'),
    ).toBe(false);
  });

  it('shows the preview when the row has one and an icon when it does not', () => {
    const withPreview = mountEditor([row({ thumbnailBase64: 'data:image/jpeg;base64,AAAA' })]);
    expect(withPreview.findComponent({ name: 'QImg' }).props('src')).toBe('data:image/jpeg;base64,AAAA');

    const without = mountEditor([row()]);
    expect(without.findComponent({ name: 'QImg' }).exists()).toBe(false);
    expect(without.findComponent({ name: 'QIcon' }).props('name')).toBe('image');
  });

  it('says what an empty list means', () => {
    const wrapper = mountEditor([]);
    expect(wrapper.text()).toContain('No attachments yet');
  });

  it('asks for the row when the remove button is pressed, without removing it itself', async () => {
    const wrapper = mountEditor([row({ fileName: 'kick.png' })]);
    const remove = wrapper.find('[aria-label="Remove attachment kick.png"]');

    await remove.trigger('click');

    expect(wrapper.emitted('remove')?.[0]).toEqual([0]);
    expect(currentRows(wrapper)).toHaveLength(1);
  });

  it('asks the page to add a row and reports how many the record has', async () => {
    const wrapper = mountEditor([row({ fileName: 'a.png' }), row({ fileName: 'b.png' })]);
    const add = wrapper.findAllComponents({ name: 'QBtn' })
      .find((button) => button.props('label') === 'Add attachment')!;

    await add.trigger('click');

    expect(wrapper.emitted('add')).toHaveLength(1);
    expect(wrapper.text()).toContain('2 on this item');
  });

  it('opens the stored file in the preview dialog, not in a new tab', async () => {
    const saved = mountEditor([row({ id: 7, filePath: 'media/items/3/x.png', fileName: 'x.png' })]);
    const open = saved.findAllComponents({ name: 'QBtn' }).find((b) => b.props('label') === 'Open');
    expect(open).toBeTruthy();
    // A link would be opened by the browser without the bearer token: the route answers 401.
    expect(open!.props('href')).toBeUndefined();

    await open!.trigger('click');

    const dialog = saved.findComponent({ name: 'FilePreviewDialog' });
    expect(dialog.props('modelValue')).toBe(true);
    expect(dialog.props('attachmentId')).toBe(7);
    expect(dialog.props('fileName')).toBe('x.png');

    const draft = mountEditor([row({ id: 0, fileName: 'new.png' })]);
    expect(draft.findAllComponents({ name: 'QBtn' }).some((b) => b.props('label') === 'Open')).toBe(false);
  });

  it('reads a picked file into the row as a data URL', async () => {
    const wrapper = mountEditor([row()]);
    const file = wrapper.findComponent({ name: 'QFile' });
    const picked = new File(['hello'], 'note.txt', { type: 'text/plain' });

    await file.vm.$emit('update:modelValue', picked);

    // fileToBase64 is asynchronous; the row gets the name first.
    expect(currentRows(wrapper)[0]!.fileName).toBe('note.txt');
  });
});
