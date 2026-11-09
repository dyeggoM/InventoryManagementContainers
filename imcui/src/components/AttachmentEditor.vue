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
      <div class="row items-center q-gutter-sm">
        <q-select
          class="col-3"
          dense
          options-dense
          label="type"
          v-model="row.type"
          :options="types"
          :rules="[(value: string) => !!value || 'Pick a type']"
        />

        <q-file
          v-if="row.id === 0"
          class="col-6"
          dense
          label="Select a file"
          :model-value="row.file ?? null"
          :rules="[() => !!(row.file || row.fileName) || 'Choose a file']"
          @update:model-value="(file: File | null) => onFile(row, file)"
        >
          <template v-slot:prepend>
            <q-icon name="attach_file" />
          </template>
        </q-file>
        <div v-else class="col-6">
          <div class="text-body2 ellipsis">{{ row.fileName || 'Unnamed file' }}</div>
          <div class="text-caption text-grey-7">Saved attachment #{{ row.id }}</div>
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
        v-if="row.type === 'Photo' && row.fileBase64"
        :src="row.fileBase64"
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
import { fileToBase64 } from '@services/utilities';

/** An attachment row that may still carry the file the user picked in this form. */
export interface AttachmentRow {
  id: number;
  itemId: number;
  type?: string | null;
  fileName?: string | null;
  filePath?: string | null;
  fileBase64?: string | null;
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

async function onFile(row: AttachmentRow, file: File | null) {
  row.file = file;
  if (!file) {
    row.fileName = '';
    row.fileBase64 = '';
    return;
  }
  row.fileName = file.name;
  row.fileBase64 = await fileToBase64(file);
}
</script>
