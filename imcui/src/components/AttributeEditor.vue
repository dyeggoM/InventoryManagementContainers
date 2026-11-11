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
    <div v-if="!readonly" class="row items-center q-gutter-x-sm q-mb-xs attribute-actions">
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
          :readonly="readonly"
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
          :readonly="readonly"
          class="attribute-name"
          placeholder="Pick an attribute"
          :model-value="row.label"
          :options="labelOptions(index)"
          :display-value="prettyLabel(row.label)"
          emit-value
          map-options
          @update:model-value="(value: string) => setLabel(index, value)"
        />
        <!-- A searchable list, not a menu: `use-input` opens the phone's keyboard. `hide-selected`
             hides the chosen value from the menu, but it also hides it from the field, so the
             input has to keep it: with `fill-input` off Quasar renders only the arrow and the
             value of the row is invisible. -->
        <q-select
          v-if="listFor(row.label).length > 0"
          dense
          options-dense
          borderless
          hide-bottom-space
          use-input
          hide-selected
          fill-input
          :input-debounce="0"
          autocomplete="off"
          inputmode="search"
          :readonly="readonly"
          :loading="creatingIndex === index"
          :ref="(element: unknown) => bindValueSelect(index, element)"
          placeholder="—"
          v-model="row.value"
          :options="valueOptions(index)"
          @filter="
            (value: string, update: (callback: () => void) => void) =>
              onValueFilter(index, value, update)
          "
          @popup-hide="clearValueFilter(index)"
        >
          <!-- The dropdown is the list; what the user types narrows it and may name a value that
               Configuration does not have yet. Choosing it saves the value there and here. -->
          <template v-slot:no-option>
            <q-item
              v-if="addCandidate(index)"
              clickable
              :disable="creatingIndex === index"
              class="attribute-add-option"
              @click="createFromList(index)"
            >
              <q-item-section avatar>
                <q-spinner v-if="creatingIndex === index" size="16px" color="primary" />
                <q-icon v-else name="add" size="18px" />
              </q-item-section>
              <q-item-section>
                Añadir «{{ addCandidate(index) }}» a «{{ prettyLabel(row.label) }}»
              </q-item-section>
            </q-item>
            <q-item v-else-if="valueFilterFor(index).trim()" class="attribute-no-match">
              <q-item-section class="text-caption text-grey-7">No matches.</q-item-section>
            </q-item>
          </template>
          <!-- Some options match but the typed string is not one of them: it still goes last. -->
          <template v-slot:after-options>
            <q-item
              v-if="addCandidate(index)"
              clickable
              :disable="creatingIndex === index"
              class="attribute-add-option"
              @click="createFromList(index)"
            >
              <q-item-section avatar>
                <q-spinner v-if="creatingIndex === index" size="16px" color="primary" />
                <q-icon v-else name="add" size="18px" />
              </q-item-section>
              <q-item-section>
                Añadir «{{ addCandidate(index) }}» a «{{ prettyLabel(row.label) }}»
              </q-item-section>
            </q-item>
          </template>
        </q-select>
        <q-input
          v-else
          dense
          borderless
          :readonly="readonly"
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
          v-if="!readonly"
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
        <!-- Adding a value to Configuration can fail: the row keeps its value and says why. -->
        <div v-if="createError && createError.index === index" class="attribute-error" role="alert">
          {{ createError.message }}
        </div>
      </div>
    </div>

  </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue';
import type { Attributes } from '@services/items';
import { prettyLabel } from '../services/utilities';

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
  /** The session cannot write: rows render as text and the add/remove buttons are gone. */
  readonly?: boolean;
  /**
   * Saves a new value to Configuration and resolves whether it was written. Only an admin and a
   * superadmin can do it, so the page passes it only for them; without it no affordance appears.
   */
  createValue?: ((label: string, value: string) => Promise<boolean>) | undefined;
}>();

const rows = defineModel<Attributes[]>({ required: true });

/** The row being edited: its value is shown raw so the caret never fights a formatter. */
const focusedIndex = ref<number | null>(null);

/** Finds one attribute among ten; matches the name or the value. */
const filter = ref('');

/** What the user typed into each value list, keyed by the row's original index. */
const valueFilters = ref<Record<number, string>>({});

/** The row whose value is being written to Configuration: its field shows a spinner meanwhile. */
const creatingIndex = ref<number | null>(null);

/** The last failed add, shown under the row it belongs to. */
const createError = ref<{ index: number; message: string } | null>(null);

/** The value selects by row index: after a failed add the field must show the old value again. */
const valueSelects = new Map<number, ValueSelectHandle>();

interface ValueSelectHandle {
  updateInputValue?: (value: string, noFiltering?: boolean, internal?: boolean) => void;
  hidePopup?: () => void;
}

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
    label: prettyLabel(label),
    value: label,
    disable: label !== own && rows.value.some((row) => row.label === label),
  }));
}



function listFor(label: string): string[] {
  return props.lists[label] ?? [];
}

/** What the user typed into a row's value list; the filter keeps the caret where they left it. */
function valueFilterFor(index: number): string {
  return valueFilters.value[index] ?? '';
}

function setValueFilter(index: number, value: string | null | undefined) {
  createError.value = null;
  valueFilters.value = { ...valueFilters.value, [index]: value == null ? '' : String(value) };
}

/**
 * Quasar's `filter` hands over an `update` callback: a select that filters its own list has to
 * call it, or the field stays "loading" (and never fills with the chosen value).
 */
function onValueFilter(
  index: number,
  value: string | null | undefined,
  update: (callback: () => void) => void,
) {
  update(() => setValueFilter(index, value));
}

/** Keeps the rendered select of each row, so a failed add can put the old text back. */
function bindValueSelect(index: number, element: unknown) {
  const handle = element as ValueSelectHandle | null;
  if (handle) valueSelects.set(index, handle);
  else valueSelects.delete(index);
}

/** Shows `value` in the field again and closes the list: what a refusal must look like. */
function restoreValueInput(index: number, value: string) {
  const handle = valueSelects.get(index);
  if (!handle) return;
  // Same call Quasar makes when the field loses focus: the label, not the typed text.
  handle.updateInputValue?.(value, true, true);
  handle.hidePopup?.();
}

/** Closing the dropdown drops the filter, so the next open starts from the whole list. */
function clearValueFilter(index: number) {
  if (valueFilters.value[index] === undefined) return;
  const { [index]: _dropped, ...rest } = valueFilters.value;
  void _dropped;
  valueFilters.value = rest;
}

/** The options to show: the whole list, or the part of it that contains what was typed. */
function valueOptions(index: number): string[] {
  const row = rows.value[index];
  if (!row) return [];
  const all = listFor(row.label);
  const needle = valueFilterFor(index).trim().toLowerCase();
  if (!needle) return all;
  return all.filter((option) => option.toLowerCase().includes(needle));
}

/**
 * The string the dropdown may offer to add, or null. Nothing is offered without `createValue`, for
 * a session that cannot write, for an empty string, or for a value the list already has.
 */
function addCandidate(index: number): string | null {
  if (props.readonly || props.createValue === undefined) return null;
  const row = rows.value[index];
  if (!row) return null;
  const typed = valueFilterFor(index).trim();
  if (!typed) return null;
  const existing = listFor(row.label).some(
    (option) => option.trim().toLowerCase() === typed.toLowerCase(),
  );
  return existing ? null : typed;
}

/**
 * Adds the typed string to Configuration and, only when that worked, to the row. A failure leaves
 * the row as it was and says so under it instead of writing half of it.
 */
async function createFromList(index: number) {
  // Disabling the select would close the list and hide the spinner: one write at a time instead.
  if (creatingIndex.value !== null) return;
  const create = props.createValue;
  const row = rows.value[index];
  const typed = addCandidate(index);
  if (!row || !typed || create === undefined) return;

  const previous = row.value;
  creatingIndex.value = index;
  createError.value = null;

  let created = false;
  try {
    created = await create(row.label, typed);
  } catch {
    // A rejected promise is the same outcome as `false`: nothing was written, `created` stays false.
  } finally {
    creatingIndex.value = null;
  }

  if (created) {
    row.value = typed;
    clearValueFilter(index);
    valueSelects.get(index)?.hidePopup?.();
    return;
  }

  row.value = previous;
  createError.value = {
    index,
    message: `Could not add “${typed}” to “${prettyLabel(row.label)}”.`,
  };
  clearValueFilter(index);
  // The typed text is not the value of the row: put the previous one back in the field.
  restoreValueInput(index, previous);
}

function setLabel(index: number, label: string) {
  const row = rows.value[index];
  if (!row) return;
  row.label = label;
  clearValueFilter(index);
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
  // Filters and errors are keyed by row index: after a removal none of them point at their row.
  valueFilters.value = {};
  valueSelects.clear();
  createError.value = null;
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

/* A message about this row sits under the whole row, inside the same grid. */
.attribute-error {
  grid-column: 1 / -1;
  font-size: 11px;
  line-height: 1.3;
  color: #b91c1c;
  padding: 2px 0 4px;
}

/* The "add to Configuration" row reads as an action, not as another value of the list. */
.attribute-add-option {
  color: var(--q-primary);
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