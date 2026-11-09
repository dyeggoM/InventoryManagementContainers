<template>
  <div class="attribute-editor">
    <div v-if="rows.length" class="attribute-head">
      <span>Attribute</span>
      <span>Value</span>
      <span aria-hidden="true"></span>
    </div>

    <div v-if="rows.length === 0" class="text-caption text-grey-7 q-mb-sm">
      No attributes on this item yet.
    </div>

    <div class="attribute-list">
      <div
        v-for="(row, index) in rows"
        :key="`${row.label}-${index}`"
        class="attribute-row"
        :class="{ 'attribute-row--wide': isLongText(row) }"
      >
        <q-select
          dense
          options-dense
          borderless
          hide-dropdown-icon
          class="attribute-name"
          placeholder="Pick an attribute"
          :model-value="row.label"
          :options="labelOptions(index)"
          :display-value="pretty(row.label)"
          emit-value
          map-options
          @update:model-value="(value: string) => setLabel(index, value)"
        />
        <q-select
          v-if="listFor(row.label).length > 0"
          dense
          options-dense
          borderless
          hide-bottom-space
          placeholder="—"
          v-model="row.value"
          :options="listFor(row.label)"
        />
        <q-input
          v-else
          dense
          borderless
          placeholder="—"
          :model-value="display(row, index)"
          :type="isLongText(row) ? 'textarea' : 'text'"
          :inputmode="isNumeric(row) ? 'decimal' : undefined"
          :autogrow="isLongText(row)"
          :maxlength="500"
          hide-bottom-space
          :input-class="isNumeric(row) && !isLongText(row) ? 'text-right' : ''"
          @focus="focusedIndex = index"
          @blur="focusedIndex = null"
          @update:model-value="(value: string | number | null) => onValue(row, index, value)"
        />
        <q-btn
          icon="delete_outline"
          size="sm"
          color="grey-7"
          flat
          round
          dense
          type="button"
          class="attribute-remove"
          :aria-label="`Remove ${row.label || 'attribute'}`"
          @click="remove(index)"
        />
      </div>
    </div>

    <div class="row items-center q-gutter-sm q-mt-sm">
      <q-btn
        flat
        no-caps
        dense
        icon="add"
        label="Add attribute"
        :color="availableLabels.length === 0 ? 'grey-6' : 'primary'"
        type="button"
        :disable="availableLabels.length === 0"
        @click="add"
      />
      <span v-if="labels.length === 0" class="text-caption text-grey-7">
        No attributes are defined in Configuration
      </span>
      <span v-else-if="availableLabels.length === 0" class="text-caption text-grey-7">
        All attributes are on this item — remove one to swap it
      </span>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue';
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

/** The row being edited: its value is shown raw so the caret never fights a formatter. */
const focusedIndex = ref<number | null>(null);

const availableLabels = computed(() =>
  props.labels.filter((label) => !rows.value.some((row) => row.label === label)),
);

function labelOptions(index: number): { label: string; value: string; disable?: boolean }[] {
  const own = rows.value[index]?.label;
  return props.labels.map((label) => ({
    label: pretty(label),
    value: label,
    disable: label !== own && rows.value.some((row) => row.label === label),
  }));
}

/** `BuyPrice` is a key in the data, but a person reads "Buy Price". */
function pretty(label: string): string {
  return label.replace(/([a-z0-9])([A-Z])/g, '$1 $2');
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

/** Adds grouping to whole numbers while the field is not focused (10,000,000). */
function display(row: Attributes, index: number): string {
  const value = row.value ?? '';
  if (focusedIndex.value === index) return value;
  return /^-?\d+$/.test(value.trim()) && value.trim().length > 3
    ? Number(value).toLocaleString('en-US')
    : value;
}

function onValue(row: Attributes, index: number, value: string | number | null) {
  const text = value == null ? '' : String(value);
  // Keep the stored value raw: digits, a decimal point/comma and a leading minus.
  row.value = text.replace(/(?!^-)[^\d.,-]/g, '');
}

function add() {
  const [next] = availableLabels.value;
  if (next) rows.value.push({ label: next, value: '' });
}

function remove(index: number) {
  rows.value.splice(index, 1);
}

/** Prices and other numeric values get the decimal keypad instead of a full keyboard. */
function isNumeric(row: Attributes): boolean {
  const value = (row.value ?? '').trim();
  return value.length > 0 && /^-?\d+([.,]\d+)?$/.test(value);
}

/** Long prose (a description, a note) gets a textarea; short values stay one line. */
function isLongText(row: Attributes): boolean {
  if ((row.value ?? '').length > 60 || (row.value ?? '').includes('\n')) return true;
  return /^(description|notes?)$/i.test(row.label);
}
</script>

<style scoped>
/* A grid, not Quasar columns: gutter margins made the third control wrap onto its own line. */
.attribute-head,
.attribute-row {
  display: grid;
  grid-template-columns: minmax(0, 2fr) minmax(0, 3fr) 40px;
  column-gap: 8px;
  align-items: center;
}

.attribute-head {
  margin-bottom: 2px;
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.08em;
  text-transform: uppercase;
  color: #6b7280;
}

.attribute-list {
  display: grid;
  row-gap: 2px;
}

.attribute-row {
  border-bottom: 1px solid rgba(0, 0, 0, 0.06);
  min-height: 44px;
}

.attribute-row :deep(.q-field__native),
.attribute-row :deep(.q-field__input) {
  font-size: 15px;
}

.attribute-name :deep(.q-field__native) {
  color: #4b5563;
  font-size: 14px;
}

.attribute-remove {
  justify-self: end;
  margin-left: 2px;
}

/* Keep the row action away from the value's dropdown arrow. */
.attribute-row > :nth-child(3) {
  padding-left: 6px;
}

.attribute-row.attribute-row--wide {
  grid-template-columns: minmax(0, 1fr) 40px;
  align-items: start;
  padding-top: 6px;
  padding-bottom: 6px;
}

.attribute-row.attribute-row--wide > :nth-child(2) {
  grid-column: 1;
}

.attribute-row.attribute-row--wide > :nth-child(3) {
  grid-column: 2;
  grid-row: 1 / span 2;
  align-self: center;
}

@media (max-width: 599px) {
  .attribute-head,
  .attribute-row {
    grid-template-columns: minmax(0, 1fr) minmax(0, 1.2fr) 36px;
    column-gap: 6px;
  }
}
</style>