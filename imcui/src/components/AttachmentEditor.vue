<template>
  <div class="attachment-editor">
    <div v-if="rows.length === 0" class="text-caption text-grey-7 q-mb-sm">
      No attachments yet — take a photo or pick a file.
    </div>

    <div class="attachment-list">
      <div v-for="(row, index) in rows" :key="`${row.id}-${index}`" class="attachment-item">
        <div class="attachment-thumb" :class="{ 'attachment-thumb--empty': !preview(row) }">
          <q-img v-if="preview(row)" :src="preview(row) ?? ''" fit="cover" />
          <q-icon v-else :name="typeIcon(row)" size="22px" />
        </div>

        <div class="attachment-body">
          <div class="text-body2 ellipsis">{{ row.fileName || 'New attachment' }}</div>

          <div class="row items-center q-gutter-xs">
            <q-select
              dense
              borderless
              options-dense
              :readonly="readonly"
              class="attachment-type"
              v-model="row.type"
              :options="types"
              :rules="[(value: string) => !!value || 'Pick a type']"
            />
            <q-btn
              v-if="row.id > 0 && row.filePath"
              flat
              dense
              size="sm"
              no-caps
              icon="open_in_new"
              label="Open"
              :href="fileUrl(row.id)"
              target="_blank"
              rel="noopener"
              class="q-px-xs"
            />
          </div>

          <div v-if="row.id === 0 && !readonly" class="column">
            <q-file
              dense
              borderless
              :label="isPhoto(row) ? 'Take a photo' : 'Select a file'"
              :accept="isPhoto(row) ? 'image/*' : undefined"
              :capture="isPhoto(row) ? 'environment' : undefined"
              :model-value="row.file ?? null"
              :rules="[() => !!(row.file || row.fileName) || 'Choose a file']"
              @update:model-value="(file: File | null) => onFile(row, file)"
            >
              <template v-slot:prepend>
                <q-icon :name="isPhoto(row) ? 'photo_camera' : 'attach_file'" />
              </template>
            </q-file>

            <!-- The camera shortcut hides the gallery; keep a way back to the camera roll. -->
            <q-btn
              v-if="isPhoto(row)"
              flat
              dense
              size="sm"
              no-caps
              icon="photo_library"
              label="Pick from gallery instead"
              class="self-start q-px-none"
              @click="pickFromGallery(row)"
            />
          </div>
        </div>

        <q-btn
          v-if="!readonly"
          icon="delete_outline"
          flat
          round
          dense
          color="grey-6"
          type="button"
          :aria-label="`Remove attachment ${row.fileName || row.id}`"
          @click="emit('remove', index)"
        />
      </div>
    </div>

    <div v-if="!readonly" class="row items-center q-gutter-sm q-mt-sm">
      <q-btn
        flat
        no-caps
        dense
        icon="add"
        label="Add attachment"
        color="primary"
        type="button"
        @click="emit('add')"
      />
      <span v-if="rows.length" class="text-caption text-grey-7">{{ rows.length }} on this item</span>
    </div>
  </div>
</template>

<script setup lang="ts">
import { onBeforeUnmount } from 'vue';
import { attachmentsService } from '@services/attachment';

/** An attachment row that may still carry the file the user picked in this form. */
export interface AttachmentRow {
  id: number;
  itemId: number;
  type?: string | null;
  fileName?: string | null;
  filePath?: string | null;
  /** Compressed preview returned by the API. */
  thumbnailBase64?: string | null;
  /** A local object URL for a file picked in this form, so the preview is instant. */
  previewUrl?: string | null;
  attributes?: string | null;
  file?: File | null;
}

defineProps<{
  /** Attachment types from the `AttachmentType` constant. */
  types: string[];
  /** The session cannot write: no picking, adding or removing files. */
  readonly?: boolean;
}>();

const rows = defineModel<AttachmentRow[]>({ required: true });

const emit = defineEmits<{
  add: [];
  remove: [index: number];
}>();

onBeforeUnmount(() => {
  for (const row of rows.value) releasePreview(row);
});

function fileUrl(id: number): string {
  return attachmentsService.fileUrl(id);
}

/** A photo is taken with the camera; every other type is a document from the file manager. */
function isPhoto(row: AttachmentRow): boolean {
  return (row.type ?? '').toLowerCase() === 'photo';
}

function preview(row: AttachmentRow): string | null {
  return row.thumbnailBase64 || row.previewUrl || null;
}

function typeIcon(row: AttachmentRow): string {
  const type = (row.type ?? '').toLowerCase();
  if (type.includes('photo')) return 'image';
  if (type.includes('manual')) return 'menu_book';
  if (type.includes('warranty')) return 'verified';
  return 'description';
}

/** Asking for a capture device makes phones skip the gallery, so this reopens it. */
function pickFromGallery(row: AttachmentRow) {
  const input = document.createElement('input');
  input.type = 'file';
  input.accept = 'image/*';
  input.addEventListener('change', () => {
    const [file] = Array.from(input.files ?? []);
    if (file) {
      void onFile(row, file);
    }
  });
  input.click();
}

/** Frees an object URL so a phone with a dozen photos does not leak them. */
function releasePreview(row: AttachmentRow) {
  if (row.previewUrl) {
    URL.revokeObjectURL(row.previewUrl);
    row.previewUrl = null;
  }
}

function onFile(row: AttachmentRow, file: File | null) {
  releasePreview(row);
  row.file = file;

  if (!file) {
    row.fileName = '';
    return;
  }

  row.fileName = file.name;
  // The browser can show the picked photo straight away; the bytes themselves only travel on save.
  if (file.type.startsWith('image/')) {
    row.previewUrl = URL.createObjectURL(file);
  }
}
</script>

<style scoped>
.attachment-list {
  display: grid;
  row-gap: 4px;
}

/* A small thumb, the facts, and the action: a list, not a gallery, so several fit on a phone. */
.attachment-item {
  display: grid;
  grid-template-columns: 48px minmax(0, 1fr) 36px;
  column-gap: 10px;
  align-items: start;
  padding: 8px 0;
  border-bottom: 1px solid rgba(0, 0, 0, 0.06);
  min-height: 56px;
}

.attachment-thumb {
  width: 48px;
  height: 48px;
  border-radius: 6px;
  overflow: hidden;
  background: #f2f4f7;
}

.attachment-thumb--empty {
  display: flex;
  align-items: center;
  justify-content: center;
  color: #8b93a1;
}

.attachment-body {
  min-width: 0;
}

.attachment-type {
  max-width: 150px;
  font-size: 13px;
}

.attachment-type :deep(.q-field__native) {
  min-height: 24px;
  padding: 0;
}
</style>