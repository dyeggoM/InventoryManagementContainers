<template>
  <div class="column q-gutter-sm">
    <div class="row items-center q-gutter-md">
      <q-btn label="Add Attachment" type="button" color="secondary" @click="emit('add')" />
      <span v-if="rows.length" class="text-caption text-grey-7">
        {{ rows.length }} on this item
      </span>
    </div>

    <div v-if="rows.length === 0" class="text-caption text-grey-7">No attachments yet.</div>

    <q-card v-for="(row, index) in rows" :key="`${row.id}-${index}`" flat bordered class="q-pa-sm">
      <div class="attachment-row">
        <q-select
          dense
          options-dense
          label="type"
          v-model="row.type"
          :options="types"
          :rules="[(value: string) => !!value || 'Pick a type']"
        />

        <div v-if="row.id === 0" class="attachment-pick">
          <q-file
            dense
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
            icon="photo_library"
            label="Pick from gallery instead"
            class="q-mt-xs"
            @click="pickFromGallery(row)"
          />
        </div>
        <div v-else class="attachment-file">
          <div class="text-body2 ellipsis">{{ row.fileName || 'Unnamed file' }}</div>
          <div class="text-caption text-grey-7 row items-center q-gutter-xs">
            <span>Saved attachment #{{ row.id }}</span>
            <q-btn
              v-if="row.filePath"
              flat
              dense
              size="sm"
              label="Open original"
              :href="fileUrl(row.id)"
              target="_blank"
              rel="noopener"
              icon="open_in_new"
              class="q-px-xs"
            />
          </div>
        </div>

        <q-btn
          icon="delete"
          flat
          round
          color="negative"
          type="button"
          :aria-label="`Remove attachment ${row.fileName || row.id}`"
          @click="emit('remove', index)"
        />
      </div>

      <q-img
        v-if="row.type === 'Photo' && (row.thumbnailBase64 || row.uploadBase64)"
        :src="row.thumbnailBase64 || row.uploadBase64 || ''"
        height="90px"
        fit="contain"
        class="q-mt-sm"
      />
      <div v-else-if="row.type === 'Photo' && row.id > 0" class="text-caption text-grey-7 q-mt-sm">
        No preview stored for this photo.
      </div>
    </q-card>
  </div>
</template>

<script setup lang="ts">
import { attachmentsService } from '@services/attachment';
import { fileToBase64 } from '@services/utilities';

/** An attachment row that may still carry the file the user picked in this form. */
export interface AttachmentRow {
  id: number;
  itemId: number;
  type?: string | null;
  fileName?: string | null;
  filePath?: string | null;
  /** Compressed preview returned by the API. */
  thumbnailBase64?: string | null;
  /** The picked file as a data URL; the API writes it to disk on save. */
  uploadBase64?: string | null;
  attributes?: string | null;
  file?: File | null;
}

defineProps<{
  /** Attachment types from the `AttachmentType` constant. */
  types: string[];
}>();

const rows = defineModel<AttachmentRow[]>({ required: true });

const emit = defineEmits<{
  add: [];
  remove: [index: number];
}>();

function fileUrl(id: number): string {
  return attachmentsService.fileUrl(id);
}

/** A photo is taken with the camera; every other type is a document from the file manager. */
function isPhoto(row: AttachmentRow): boolean {
  return (row.type ?? '').toLowerCase() === 'photo';
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

async function onFile(row: AttachmentRow, file: File | null) {
  row.file = file;
  if (!file) {
    row.fileName = '';
    row.uploadBase64 = '';
    return;
  }
  row.fileName = file.name;
  // The API compresses this into a thumbnail and writes the original to disk.
  row.uploadBase64 = await fileToBase64(file);
}
</script>

<style scoped>
.attachment-row {
  display: grid;
  grid-template-columns: 120px minmax(0, 1fr) auto;
  column-gap: 8px;
  align-items: center;
}

/* The picker and its gallery shortcut stack inside their cell. */
.attachment-pick {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
}

/* Phone: type above the file picker, delete on the right of both. */
@media (max-width: 599px) {
  .attachment-row {
    grid-template-columns: minmax(0, 1fr) auto;
    row-gap: 4px;
  }

  .attachment-row > :nth-child(2) {
    grid-column: 1;
  }

  .attachment-row > :nth-child(3) {
    grid-column: 2;
    grid-row: 1 / span 2;
    align-self: center;
  }
}
</style>