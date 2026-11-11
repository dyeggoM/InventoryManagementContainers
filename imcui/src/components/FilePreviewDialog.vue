<template>
  <q-dialog
    v-model="visible"
    :maximized="$q.screen.lt.sm"
    transition-show="fade"
    transition-hide="fade"
  >
    <q-card class="preview-card column no-wrap">
      <q-toolbar class="preview-header q-px-md">
        <q-toolbar-title class="column">
          <span class="text-subtitle1 text-weight-medium ellipsis">{{ title }}</span>
          <span v-if="type" class="text-caption text-grey-7 ellipsis">{{ type }}</span>
        </q-toolbar-title>

        <q-btn
          v-if="blob"
          icon="download"
          flat
          round
          dense
          aria-label="Descargar"
          @click="download"
        />
        <q-btn icon="close" flat round dense aria-label="Cerrar" @click="close" />
      </q-toolbar>

      <q-separator />

      <q-card-section class="col column no-wrap preview-body">
        <div v-if="loading" class="preview-state column flex-center">
          <q-spinner color="primary" size="2.5em" aria-label="Cargando" />
          <div class="text-caption text-grey-7 q-mt-sm">Cargando el archivo…</div>
        </div>

        <div v-else-if="errorMessage" class="preview-state column flex-center text-center">
          <q-icon name="error_outline" size="32px" color="negative" />
          <div class="text-body2 q-mt-sm">{{ errorMessage }}</div>
          <q-btn flat no-caps label="Reintentar" class="q-mt-sm" @click="load" />
        </div>

        <div v-else-if="kind === 'image' && objectUrl" class="preview-figure">
          <img class="preview-image" :src="objectUrl" :alt="title" />
        </div>

        <iframe
          v-else-if="kind === 'pdf' && objectUrl"
          class="preview-frame"
          :src="objectUrl"
          :title="title"
        />

        <!-- A format the browser cannot render is downloaded instead of pretending otherwise. -->
        <div v-else-if="blob" class="preview-state column flex-center text-center">
          <q-icon name="description" size="40px" color="grey-6" />
          <div class="text-body2 q-mt-sm">Este archivo no se puede mostrar aquí.</div>
          <q-btn
            unelevated
            no-caps
            color="primary"
            icon="download"
            label="Descargar"
            class="q-mt-md"
            @click="download"
          />
        </div>
      </q-card-section>
    </q-card>
  </q-dialog>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { attachmentsService } from '../services/attachment';
import { getErrorMessage } from '../services/api';

const props = defineProps<{
  /** The attachment whose original is shown. Only read when the dialog opens. */
  attachmentId: number;
  /** The name the API stored, used as the title and as the download file name. */
  fileName?: string | null;
  /** The attachment type from the `AttachmentType` constant; only a hint for the renderer. */
  type?: string | null;
}>();

/** `v-model`: the dialog asks to be closed, it does not decide it for the page. */
const visible = defineModel<boolean>({ default: false });

const IMAGE_EXTENSIONS = ['jpg', 'jpeg', 'png', 'gif', 'webp', 'bmp', 'heic', 'heif', 'avif'];

const blob = ref<Blob | null>(null);
const objectUrl = ref<string | null>(null);
const loading = ref(false);
const errorMessage = ref<string | null>(null);

/** Bumped on every load and on close, so a slow answer cannot paint the file after the fact. */
let loadToken = 0;

const title = computed(() => props.fileName?.trim() || `Adjunto #${props.attachmentId}`);

/** The text after the last dot, or nothing: a name without an extension says nothing about bytes. */
function extensionOf(name: string): string {
  const dot = name.lastIndexOf('.');
  return dot >= 0 ? name.slice(dot + 1) : '';
}

/**
 * What the browser can actually render. The server labels the bytes from the stored extension and
 * answers `application/octet-stream` when it cannot tell, so the name is the fallback.
 */
const kind = computed<'image' | 'pdf' | 'other'>(() => {
  const mime = (blob.value?.type ?? '').toLowerCase();
  if (mime.startsWith('image/')) return 'image';
  if (mime === 'application/pdf') return 'pdf';

  const extension = extensionOf((props.fileName ?? '').toLowerCase());
  if (IMAGE_EXTENSIONS.includes(extension)) return 'image';
  if (extension === 'pdf') return 'pdf';
  return 'other';
});

watch(visible, (isOpen) => {
  if (isOpen) {
    pushHistoryEntry();
    void load();
    return;
  }
  reset();
  dropHistoryEntry();
});

// The same dialog instance can be reused for another row: a new id while open reloads it.
watch(
  () => props.attachmentId,
  () => {
    if (visible.value) void load();
  },
);

onMounted(() => {
  if (typeof window !== 'undefined') window.addEventListener('popstate', onPopState);
});

onBeforeUnmount(() => {
  if (typeof window !== 'undefined') window.removeEventListener('popstate', onPopState);
  // Back navigation is left alone here: an unmount is usually the page change that caused it.
  loadToken += 1;
  releaseObjectUrl();
});

/** Nothing is downloaded until the dialog is opened. */
async function load() {
  releaseObjectUrl();
  blob.value = null;
  errorMessage.value = null;

  if (props.attachmentId <= 0) {
    errorMessage.value = 'Este adjunto todavía no tiene archivo.';
    return;
  }

  const token = ++loadToken;
  loading.value = true;
  try {
    const data = await attachmentsService.fetchFile(props.attachmentId);
    if (token !== loadToken) return;
    blob.value = data;
    // One object URL for both the preview and the download: revoked when the dialog closes.
    objectUrl.value = URL.createObjectURL(data);
  } catch (error) {
    if (token !== loadToken) return;
    errorMessage.value = getErrorMessage(error);
  } finally {
    if (token === loadToken) loading.value = false;
  }
}

function reset() {
  loadToken += 1;
  loading.value = false;
  errorMessage.value = null;
  blob.value = null;
  releaseObjectUrl();
}

function close() {
  visible.value = false;
}

/** Saves the bytes that are already in memory, under the name the API kept. */
function download() {
  const url = objectUrl.value;
  if (!url) return;

  const link = document.createElement('a');
  link.href = url;
  link.download = title.value;
  link.rel = 'noopener';
  document.body.appendChild(link);
  link.click();
  link.remove();
}

function releaseObjectUrl() {
  if (!objectUrl.value) return;
  URL.revokeObjectURL(objectUrl.value);
  objectUrl.value = null;
}

let pushedHistoryEntry = false;

/**
 * The phone's back gesture must close the dialog, not leave the page behind it. One history entry
 * marks "a dialog is open"; the gesture consumes it and the dialog closes without touching history
 * again, while the close button unwinds the same entry.
 */
function pushHistoryEntry() {
  if (typeof window === 'undefined' || pushedHistoryEntry) return;
  const current: unknown = window.history.state;
  const state =
    current && typeof current === 'object'
      ? { ...(current as Record<string, unknown>), imcFilePreview: true }
      : { imcFilePreview: true };
  try {
    window.history.pushState(state, '');
    pushedHistoryEntry = true;
  } catch {
    // A refused pushState only costs the back gesture, not the preview.
    pushedHistoryEntry = false;
  }
}

function dropHistoryEntry() {
  if (!pushedHistoryEntry) return;
  pushedHistoryEntry = false;
  try {
    window.history.back();
  } catch {
    // jsdom refuses to navigate; the dialog is closed either way.
  }
}

function onPopState() {
  if (!visible.value) return;
  // The gesture already consumed our entry: close the dialog and do not go back a second time.
  pushedHistoryEntry = false;
  visible.value = false;
}
</script>

<style scoped>
/* Full height on a phone (the dialog is maximized there), a large card on a desktop. */
.preview-card {
  width: 100%;
  max-width: 960px;
  height: 100%;
  max-height: 90vh;
}

.preview-header {
  min-height: 52px;
}

.preview-body {
  min-height: 0;
  overflow: auto;
  padding: 12px;
}

.preview-state {
  flex: 1;
  min-height: 160px;
}

.preview-figure {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 0;
}

/* `contain`, not `cover`: a photo of a part must be readable, not cropped. */
.preview-image {
  max-width: 100%;
  max-height: 100%;
  object-fit: contain;
}

.preview-frame {
  flex: 1;
  width: 100%;
  min-height: 60vh;
  border: 0;
}
</style>
