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

    <!-- Adding a row must not require scrolling past the rows: the actions sit above the table. -->
    <div class="row items-center q-gutter-x-sm q-mb-xs attribute-actions">
      <q-btn
        flat
        no-caps
        icon="add"
        label="Add attribute"
        color="primary"
        type="button"
        :disable="availableLabels.length === 0"
        @click="add"
      />
      <q-btn
        flat
        no-caps
        icon="playlist_add"
        label="Add custom"
        color="primary"
        type="button"
        @click="addCustom"
      />
      <span class="text-caption text-grey-7" v-if="availableLabels.length === 0 && labels.length > 0">
        all listed
      </span>
    </div>

    <!-- Ten rows of attributes scroll past the fold; a filter finds one without scrolling. -->
    <div v-if="rows.length > 5" class="row items-center no-wrap q-mb-xs attribute-filter">
      <q-input
        v-model="filter"
        dense
        borderless
        placeholder="Filter attributes"
        class="col"
        input-class="text-caption"
        clearable
      >
        <template v-slot:prepend>
          <q-icon name="search" size="18px" />
        </template>
      </q-input>
      <span v-if="filter.trim()" class="text-caption text-grey-7 q-ml-sm">
        {{ visibleRows.length }} of {{ rows.length }}
      </span>
    </div>

    <div v-if="filter.trim() && visibleRows.length === 0" class="text-caption text-grey-7 q-mb-sm">
      Nothing matches “{{ filter }}”.
    </div>

    <div class="attribute-list">
      <div
        v-for="{ row, index } in visibleRows"
        :key="`${row.label}-${index}`"
        class="attribute-row"
        :class="{ 'attribute-row--wide': isLongText(row) }"
      >
        <q-input
          v-if="isCustom(row)"
          dense
          borderless
          hide-bottom-space
          class="attribute-name attribute-name--custom"
          placeholder="Attribute name"
          :model-value="row.label"
          :maxlength="40"
          @update:model-value="(value: string | number | null) => setCustomLabel(index, value)"
        >
          <template v-slot:append>
            <q-icon name="edit_note" size="16px" class="attribute-name__hint" />
          </template>
        </q-input>
        <q-select
          v-else
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
          :placeholder="isDate(row) ? '' : '—'"
          :model-value="display(row, index)"
          :type="inputType(row)"
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
  /** label -> data type (Text, Number, Money, Date, LongText), set in Configuration. */
  types?: Record<string, string | null | undefined>;
}>();

const rows = defineModel<Attributes[]>({ required: true });

/** The row being edited: its value is shown raw so the caret never fights a formatter. */
const focusedIndex = ref<number | null>(null);

/** Finds one attribute among ten; matches the name or the value. */
const filter = ref('');

/** Pairs keep the original index, so editing a filtered row still targets the right entry. */
const visibleRows = computed(() => {
  const needle = filter.value.trim().toLowerCase();
  const pairs = rows.value.map((row, index) => ({ row, index }));
  if (!needle) return pairs;
  return pairs.filter(
    ({ row }) =>
      row.label.toLowerCase().includes(needle) || (row.value ?? '').toLowerCase().includes(needle),
  );
});

const availableLabels = computed(() =>
  props.labels.filter((label) => !rows.value.some((row) => row.label === label)),
);

/** A row the catalogue cannot name: typed by hand, stored on this item only. */
function isCustom(row: Attributes): boolean {
  return !props.labels.includes(row.label);
}

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
  void index;
  const text = value == null ? '' : String(value);
  // Only a numeric field is scrubbed: a string like SN-0042 must survive as typed.
  row.value = isNumeric(row) ? text.replace(/(?!^-)[^\d.,-]/g, '') : text;
}

function add() {
  const [next] = availableLabels.value;
  if (next) rows.value.push({ label: next, value: '' });
}

/** Starts a row with an empty key: the user types the name the catalogue does not have. */
function addCustom() {
  const row: Attributes = { label: '', value: '' };
  rows.value.push(row);
}

/** Keys stay close to what a person typed: letters, digits, spaces, dash and underscore. */
function setCustomLabel(index: number, value: string | number | null) {
  const row = rows.value[index];
  if (!row) return;
  row.label = (value == null ? '' : String(value)).replace(/[^A-Za-z0-9 _-]/g, '');
}

function remove(index: number) {
  rows.value.splice(index, 1);
}

/** The data type of a row: from Configuration for a listed attribute, Text for one typed by hand. */
function typeOf(row: Attributes): string {
  return props.types?.[row.label] ?? 'Text';
}

/** Money and Number get the decimal keypad, digits only and right alignment. */
function isNumeric(row: Attributes): boolean {
  const type = typeOf(row);
  return type === 'Money' || type === 'Number';
}

/** Long prose gets a textarea spanning the full row. */
function isLongText(row: Attributes): boolean {
  if (typeOf(row) === 'LongText') return true;
  // A hand-typed value that grew long is prose whatever its type says.
  return isCustom(row) && ((row.value ?? '').length > 60 || (row.value ?? '').includes('\n'));
}

/** Dates open the calendar the device already has instead of a plain text box. */
function isDate(row: Attributes): boolean {
  return typeOf(row) === 'Date';
}

function inputType(row: Attributes): 'textarea' | 'date' | 'text' {
  if (isLongText(row)) return 'textarea';
  if (isDate(row)) return 'date';
  return 'text';
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
  margin-top: 6px;
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

.attribute-actions {
  min-height: 40px;
}

.attribute-actions :deep(.q-btn) {
  padding: 0 8px; /* a 40px row keeps the tap target without a dense 23px button */
}

.attribute-filter :deep(.q-field__native) {
  font-size: 13px;
}

.attribute-name--custom :deep(.q-field__native) {
  font-style: italic;
  color: #334155;
}

.attribute-name__hint {
  color: #b6bcc6;
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