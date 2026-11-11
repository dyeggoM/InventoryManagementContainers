import { defineComponent, h } from 'vue';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import FilePreviewDialog from './FilePreviewDialog.vue';

// The dialog's job is when it fetches and what it renders, so the service is replaced and the calls
// are asserted directly.
const mocks = vi.hoisted(() => ({ fetchFile: vi.fn() }));

vi.mock('@services/attachment', () => ({
  attachmentsService: { fetchFile: mocks.fetchFile },
}));

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
        { class: 'q-dialog-stub', 'data-maximized': String(props.maximized) },
        props.modelValue ? slots.default?.() : [],
      );
  },
});

const QCard = defineComponent({
  name: 'QCard',
  setup(_props, { slots }) {
    return () => h('div', { class: 'q-card-stub' }, slots.default?.());
  },
});

const QCardSection = QCard;
const QToolbar = QCard;

const QToolbarTitle = defineComponent({
  name: 'QToolbarTitle',
  setup(_props, { slots }) {
    return () => h('div', { class: 'q-toolbar-title-stub' }, slots.default?.());
  },
});

const QSeparator = defineComponent({ name: 'QSeparator', render: () => h('hr') });
const QSpinner = defineComponent({
  name: 'QSpinner',
  render: () => h('div', { class: 'q-spinner-stub' }),
});

const QIcon = defineComponent({
  name: 'QIcon',
  props: { name: { type: String, default: '' } },
  setup(props) {
    return () => h('i', { class: 'q-icon-stub', 'data-icon': props.name });
  },
});

const QBtn = defineComponent({
  name: 'QBtn',
  props: {
    label: { type: String, default: '' },
    icon: { type: String, default: '' },
    ariaLabel: { type: String, default: '' },
  },
  emits: ['click'],
  setup(props, { emit }) {
    return () =>
      h(
        'button',
        {
          'data-icon': props.icon,
          'aria-label': props.ariaLabel,
          'data-label': props.label,
          onClick: (event: MouseEvent) => emit('click', event),
        },
        props.label,
      );
  },
});

const stubs = {
  QDialog,
  QCard,
  QCardSection,
  QToolbar,
  QToolbarTitle,
  QSeparator,
  QSpinner,
  QIcon,
  QBtn,
};

const createObjectURL = vi.fn(() => 'blob:preview-1');
const revokeObjectURL = vi.fn();

function imageBlob(): Blob {
  return new Blob(['png'], { type: 'image/png' });
}

function mountDialog(props: Record<string, unknown> = {}): VueWrapper {
  return mount(FilePreviewDialog, {
    props: { modelValue: false, attachmentId: 7, fileName: 'kick.png', type: 'Photo', ...props },
    global: {
      stubs,
      mocks: { $q: { screen: { lt: { sm: false } } } },
    },
  });
}

async function open(wrapper: VueWrapper): Promise<void> {
  await wrapper.setProps({ modelValue: true });
  await flushPromises();
}

beforeEach(() => {
  mocks.fetchFile.mockReset();
  createObjectURL.mockClear();
  revokeObjectURL.mockClear();
  Object.defineProperty(URL, 'createObjectURL', {
    value: createObjectURL,
    configurable: true,
    writable: true,
  });
  Object.defineProperty(URL, 'revokeObjectURL', {
    value: revokeObjectURL,
    configurable: true,
    writable: true,
  });
});

describe('FilePreviewDialog', () => {
  it('downloads nothing until it is opened', () => {
    const wrapper = mountDialog();

    expect(mocks.fetchFile).not.toHaveBeenCalled();
    expect(wrapper.find('img').exists()).toBe(false);
  });

  it('fetches the original once when it opens and shows it from an object URL', async () => {
    mocks.fetchFile.mockResolvedValue(imageBlob());
    const wrapper = mountDialog();

    await open(wrapper);

    expect(mocks.fetchFile).toHaveBeenCalledTimes(1);
    expect(mocks.fetchFile).toHaveBeenCalledWith(7);
    expect(createObjectURL).toHaveBeenCalledTimes(1);
    expect(wrapper.find('img').attributes('src')).toBe('blob:preview-1');
    expect(wrapper.text()).toContain('kick.png');
    expect(wrapper.text()).toContain('Photo');

    // Closing drops the bytes; opening again fetches them a second time.
    await wrapper.setProps({ modelValue: false });
    await wrapper.setProps({ modelValue: true });
    await flushPromises();
    expect(mocks.fetchFile).toHaveBeenCalledTimes(2);
  });

  it('shows a spinner while the bytes travel', async () => {
    let resolve: ((value: Blob) => void) | undefined;
    mocks.fetchFile.mockReturnValue(new Promise<Blob>((done) => (resolve = done)));
    const wrapper = mountDialog();

    await wrapper.setProps({ modelValue: true });
    await wrapper.vm.$nextTick();

    expect(wrapper.findComponent({ name: 'QSpinner' }).exists()).toBe(true);

    resolve?.(imageBlob());
    await flushPromises();
    expect(wrapper.findComponent({ name: 'QSpinner' }).exists()).toBe(false);
  });

  it('renders a PDF in a frame instead of an image', async () => {
    mocks.fetchFile.mockResolvedValue(new Blob(['pdf'], { type: 'application/pdf' }));
    const wrapper = mountDialog({ fileName: 'manual.pdf', type: 'User Manual' });

    await open(wrapper);

    expect(wrapper.find('iframe').attributes('src')).toBe('blob:preview-1');
    expect(wrapper.find('img').exists()).toBe(false);
  });

  it('falls back to the stored name when the server cannot label the bytes', async () => {
    // AttachmentStorage answers application/octet-stream for an extension it does not map.
    mocks.fetchFile.mockResolvedValue(new Blob(['heic'], { type: 'application/octet-stream' }));
    const wrapper = mountDialog({ fileName: 'PHOTO.HEIC', type: 'Photo' });

    await open(wrapper);

    expect(wrapper.find('img').attributes('src')).toBe('blob:preview-1');
  });

  it('offers a download for a file the browser cannot render', async () => {
    mocks.fetchFile.mockResolvedValue(
      new Blob(['docx'], {
        type: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      }),
    );
    const wrapper = mountDialog({ fileName: 'manual.docx', type: 'User Manual' });

    await open(wrapper);

    expect(wrapper.find('img').exists()).toBe(false);
    expect(wrapper.find('iframe').exists()).toBe(false);
    expect(wrapper.text()).toContain('no se puede mostrar');
    expect(
      wrapper.findAllComponents({ name: 'QBtn' }).some((b) => b.props('label') === 'Descargar'),
    ).toBe(true);
    // The toolbar keeps the same action reachable for a rendered file too.
    expect(wrapper.find('[aria-label="Descargar"]').exists()).toBe(true);
  });

  it('names the downloaded file after the attachment', async () => {
    mocks.fetchFile.mockResolvedValue(imageBlob());
    const wrapper = mountDialog({ fileName: 'kick.png' });
    const clicked: { href: string; download: string }[] = [];
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (
      this: HTMLAnchorElement,
    ) {
      clicked.push({ href: this.href, download: this.download });
    });

    try {
      await open(wrapper);
      await wrapper.find('[aria-label="Descargar"]').trigger('click');
    } finally {
      click.mockRestore();
    }

    expect(clicked).toHaveLength(1);
    expect(clicked[0]!.download).toBe('kick.png');
  });

  it('shows the message the API sent when the file cannot be read', async () => {
    mocks.fetchFile.mockRejectedValue(new Error('Adjunto no encontrado'));
    const wrapper = mountDialog();

    await open(wrapper);

    expect(wrapper.text()).toContain('Adjunto no encontrado');
    expect(
      wrapper.findAllComponents({ name: 'QBtn' }).some((b) => b.props('label') === 'Reintentar'),
    ).toBe(true);
  });

  it('revokes the object URL when it closes, not before', async () => {
    mocks.fetchFile.mockResolvedValue(imageBlob());
    const wrapper = mountDialog();

    await open(wrapper);
    expect(revokeObjectURL).not.toHaveBeenCalled();

    await wrapper.setProps({ modelValue: false });
    await flushPromises();

    expect(revokeObjectURL).toHaveBeenCalledWith('blob:preview-1');
    expect(wrapper.find('img').exists()).toBe(false);
  });

  it('asks to be closed from its own close button', async () => {
    mocks.fetchFile.mockResolvedValue(imageBlob());
    const wrapper = mountDialog();

    await open(wrapper);
    await wrapper.find('[aria-label="Cerrar"]').trigger('click');
    await flushPromises();

    expect(wrapper.emitted('update:modelValue')).toEqual([[false]]);
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:preview-1');
  });

  it('closes on the phone back gesture, after pushing the entry that gesture consumes', async () => {
    mocks.fetchFile.mockResolvedValue(imageBlob());
    const push = vi.spyOn(window.history, 'pushState');
    const wrapper = mountDialog();

    await open(wrapper);
    expect(push).toHaveBeenCalledTimes(1);
    push.mockRestore();

    window.dispatchEvent(new Event('popstate'));
    await flushPromises();

    expect(wrapper.emitted('update:modelValue')).toEqual([[false]]);
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:preview-1');
  });

  it('goes full screen on a phone', () => {
    const mobile = mount(FilePreviewDialog, {
      props: { modelValue: true, attachmentId: 7, fileName: 'kick.png' },
      global: { stubs, mocks: { $q: { screen: { lt: { sm: true } } } } },
    });

    expect(mobile.findComponent({ name: 'QDialog' }).props('maximized')).toBe(true);
  });
});
