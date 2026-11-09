<template>
  <div class="attribute-editor">
    <div class="row items-center q-gutter-md q-mb-sm">
      <q-btn
        label="Add Attribute"
        type="button"
        :color="availableLabels.length === 0 ? 'grey-6' : 'secondary'"
        :disable="availableLabels.length === 0"
        @click="add"
      />
      <span v-if="labels.length === 0" class="text-caption text-grey-7">
        No attributes are defined in Configuration
      </span>
      <span v-else-if="availableLabels.length === 0" class="text-caption text-grey-7">
        Every attribute is already on this item
      </span>
    </div>

    <div class="attribute-list">
      <div v-for="(row, index) in rows" :key="`${row.label}-${index}`" class="attribute-row">
        <q-select
          dense
          options-dense
          label="attribute"
          :model-value="row.label"
          :options="labelOptions(index)"
          :rules="[(value: string) => !!value || 'Pick an attribute']"
          emit-value
          map-options
          @update:model-value="(value: string) => setLabel(index, value)"
        />
        <q-select
          v-if="listFor(row.label).length > 0"
          dense
          options-dense
          label="value"
          v-model="row.value"
          :options="listFor(row.label)"
          :rules="[maxLength]"
        />
        <q-input
          v-else
          dense
          label="value"
          v-model="row.value"
          type="textarea"
          autogrow
          :rules="[maxLength]"
        />
        <q-btn
          icon="remove"
          color="negative"
          flat
          round
          type="button"
          :aria-label="`Remove ${row.label || 'attribute'}`"
          @click="remove(index)"
        />
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import type { Attributes } from '@services/items';

/**
 * The attribute rows of an item form. The labels and their allowed values come from the
 * constants tree, so a typo cannot invent a new attribute and the value control follows the
 * data: a label with children is a choice list, anything else is free text.
 */
const props = defineProps<{
  /** Attribute labels from the `Attribute` constant, in sort order. */
  labels: string[];
  /** label -> allowed values, taken from the constants whose group is that label. */
  lists: Record<string, string[]>;
}>();

const rows = defineModel<Attributes[]>({ required: true });

const availableLabels = computed(() =>
  props.labels.filter((label) => !rows.value.some((row) => row.label === label)),
);

function labelOptions(index: number): { label: string; value: string; disable?: boolean }[] {
  const own = rows.value[index]?.label;
  return props.labels.map((label) => ({
    label,
    value: label,
    disable: label !== own && rows.value.some((row) => row.label === label),
  }));
}

function listFor(label: string): string[] {
  return props.lists[label] ?? [];
}

function setLabel(index: number, label: string) {
  const row = rows.value[index];
  if (!row) return;
  row.label = label;
  // A value chosen from another attribute's list no longer makes sense.
  if (!listFor(label).includes(row.value)) row.value = '';
}

function add() {
  const [next] = availableLabels.value;
  if (next) rows.value.push({ label: next, value: '' });
}

function remove(index: number) {
  rows.value.splice(index, 1);
}

function maxLength(value: string): boolean | string {
  return (value ?? '').length <= 500 || 'Max 500 characters';
}
</script>
<style scoped>
/* A grid, not Quasar columns: the gutter margins made the third control wrap onto its own line
   and every row reserved two lines of height. */
.attribute-list {
  display: grid;
  row-gap: 8px;
}

.attribute-row {
  display: grid;
  grid-template-columns: minmax(0, 2fr) minmax(0, 3fr) auto;
  column-gap: 8px;
  align-items: start;
}
</style>
