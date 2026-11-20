<template>
  <div class="attribute-editor">
    <!-- Alignment only: each control below names itself, so announcing "Attribute / Value" above
         them would say it twice. -->
    <div v-if="rows.length" class="attribute-head" aria-hidden="true">
      <span>Atributo</span>
      <span>Valor</span>
      <span aria-hidden="true"></span>
    </div>

    <div v-if="rows.length === 0" class="text-caption text-grey-7 q-mb-sm">
      Este ítem todavía no tiene atributos.
    </div>

    <!-- Adding a row must not require scrolling past the rows: the actions sit above the table. -->
    <div v-if="!readonly" class="row items-center q-gutter-x-sm q-mb-xs attribute-actions">
      <q-btn
        flat
        no-caps
        icon="add"
        label="Añadir atributo"
        color="primary"
        type="button"
        :disable="availableAttributes.length === 0"
        @click="add"
      />
      <q-btn
        flat
        no-caps
        icon="playlist_add"
        label="Añadir personalizado"
        color="primary"
        type="button"
        @click="addCustom"
      />
      <span
        class="text-caption text-grey-7"
        v-if="availableAttributes.length === 0 && attributes.length > 0"
      >
        ya están todos
      </span>
    </div>

    <!-- Ten rows of attributes scroll past the fold; a filter finds one without scrolling. -->
    <div v-if="rows.length > 5" class="row items-center no-wrap q-mb-xs attribute-filter">
      <q-input
        v-model="filter"
        dense
        borderless
        placeholder="Filtrar atributos"
        aria-label="Filtrar atributos"
        class="col"
        input-class="text-caption"
        clearable
      >
        <template v-slot:prepend>
          <q-icon name="search" size="18px" />
        </template>
      </q-input>
      <span v-if="filter.trim()" class="text-caption text-grey-7 q-ml-sm">
        {{ visibleRows.length }} de {{ rows.length }}
      </span>
    </div>

    <div v-if="filter.trim() && visibleRows.length === 0" class="text-caption text-grey-7 q-mb-sm">
      Nada coincide con «{{ filter }}».
    </div>

    <div class="attribute-list">
      <div
        v-for="{ row, index } in visibleRows"
        :key="`${row.code}-${index}`"
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
          placeholder="Nombre del atributo"
          aria-label="Nombre del atributo"
          :model-value="row.code"
          :maxlength="40"
          @update:model-value="(value: string | number | null) => setCustomCode(index, value)"
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
          placeholder="Elige un atributo"
          aria-label="Nombre del atributo"
          :model-value="row.code"
          :options="labelOptions(index)"
          :display-value="attributeLabel(row)"
          option-label="label"
          option-value="value"
          emit-value
          map-options
          @update:model-value="(value: string) => setAttributeCode(index, value)"
        />
        <!-- A searchable list, not a menu: `use-input` opens the phone's keyboard. `hide-selected`
             hides the chosen value from the menu, but it also hides it from the field, so the
             input has to keep it: with `fill-input` off Quasar renders only the arrow and the
             value of the row is invisible. -->
        <q-select
          v-if="listFor(row.code).length > 0"
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
          :aria-label="valueName(row)"
          :model-value="row.value"
          :options="valueOptions(index)"
          option-label="label"
          option-value="code"
          emit-value
          map-options
          @update:model-value="(value: string) => setValue(row, value)"
          @filter="
            (value: string, update: (callback: () => void) => void) =>
              onValueFilter(index, value, update)
          "
          @popup-hide="clearValueFilter(index)"
        >
          <!-- The dropdown is the list; what the user types narrows it and may name a value that
               Configuration does not have yet. Choosing it saves the value there and here, and the
               row keeps the code the server assigned, never the text that was typed. -->
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
                Añadir «{{ addCandidate(index) }}» a «{{ attributeLabel(row) }}»
              </q-item-section>
            </q-item>
            <q-item v-else-if="valueFilterFor(index).trim()" class="attribute-no-match">
              <q-item-section class="text-caption text-grey-7">Sin coincidencias.</q-item-section>
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
                Añadir «{{ addCandidate(index) }}» a «{{ attributeLabel(row) }}»
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
          :aria-label="valueName(row)"
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
          :aria-label="`Quitar el atributo ${attributeLabel(row) || 'sin nombre'}`"
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
import type { CatalogueEntry } from '@services/constants';
import { prettyLabel, UI_LOCALE } from '../services/utilities';

/**
 * The attribute rows of an item form.
 *
 * A row holds a **code** and a stored value; the catalogue turns both into something readable. The
 * codes are what an item keeps — `{"Brand": "Part"}` — and the labels («Marca», «Pieza») are what
 * the screen shows, so renaming a constant in Configuration changes no record. The value control
 * follows the data: a code with values in Configuration is a choice list, anything else is text.
 */
const props = defineProps<{
  /** The attributes Configuration defines, in its order: code for the data, label for the screen. */
  attributes: CatalogueEntry[];
  /** attribute code -> allowed values, each a code with the label the select shows. */
  lists: Record<string, CatalogueEntry[]>;
  /** attribute code -> data type (Text, Number, Money, Date, LongText), set in Configuration. */
  types?: Record<string, string | null | undefined>;
  /** The session cannot write: rows render as text and the add/remove buttons are gone. */
  readonly?: boolean;
  /**
   * Saves a new value to Configuration and answers the entry it stored: the code is the server's to
   * assign. Only an admin and a superadmin can do it, so the page passes it only for them; without
   * it no affordance appears.
   */
  createValue?:
    ((attributeCode: string, label: string) => Promise<CatalogueEntry | null>) | undefined;
}>();

const rows = defineModel<Attributes[]>({ required: true });

/** code -> label, of the attributes and of every value of every list: what a code looks like. */
const labelsByCode = computed(() => {
  const map = new Map<string, string>();
  for (const entry of props.attributes) map.set(entry.code, entry.label);
  for (const values of Object.values(props.lists)) {
    for (const entry of values) if (!map.has(entry.code)) map.set(entry.code, entry.label);
  }
  return map;
});

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

/** The label of a row's code. A code the catalogue does not know is read as it is written. */
function attributeLabel(row: Attributes): string {
  return labelsByCode.value.get(row.code) ?? (prettyLabel(row.code) || row.code);
}

/** Pairs keep the original index, so editing a filtered row still targets the right entry. */
const visibleRows = computed(() => {
  const needle = filter.value.trim().toLowerCase();
  const pairs = rows.value.map((row, index) => ({ row, index }));
  if (!needle) return pairs;
  // The user types labels («pieza»), so the filter reads labels too — not only the stored codes.
  return pairs.filter(
    ({ row }) =>
      attributeLabel(row).toLowerCase().includes(needle) ||
      (row.value ?? '').toLowerCase().includes(needle) ||
      valueLabelOf(row, row.value).toLowerCase().includes(needle),
  );
});

const availableAttributes = computed(() =>
  props.attributes.filter((entry) => !rows.value.some((row) => row.code === entry.code)),
);

/** A row the catalogue cannot name: typed by hand, stored on this item only. */
function isCustom(row: Attributes): boolean {
  return !props.attributes.some((entry) => entry.code === row.code);
}

/**
 * What a value field is called. "—" is a placeholder, not a name: without this the reader says
 * "text box" and the user has to remember which attribute the row belongs to.
 */
function valueName(row: Attributes): string {
  return attributeLabel(row) || 'Valor';
}

function labelOptions(index: number): { label: string; value: string; disable?: boolean }[] {
  const own = rows.value[index]?.code;
  return props.attributes.map((entry) => ({
    label: entry.label,
    value: entry.code,
    disable: entry.code !== own && rows.value.some((row) => row.code === entry.code),
  }));
}

function listFor(code: string): CatalogueEntry[] {
  return props.lists[code] ?? [];
}

/** The label a stored value shows; a value its list does not carry is shown as it is stored. */
function valueLabelOf(row: Attributes, code: string): string {
  if (!code) return '';
  const entry = listFor(row.code).find((option) => option.code === code);
  return entry?.label ?? labelsByCode.value.get(code) ?? code;
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

/** Shows `label` in the field again and closes the list: what a refusal must look like. */
function restoreValueInput(index: number, label: string) {
  const handle = valueSelects.get(index);
  if (!handle) return;
  // Same call Quasar makes when the field loses focus: the label, not the stored code.
  handle.updateInputValue?.(label, true, true);
  handle.hidePopup?.();
}

/** Closing the dropdown drops the filter, so the next open starts from the whole list. */
function clearValueFilter(index: number) {
  if (valueFilters.value[index] === undefined) return;
  const { [index]: _dropped, ...rest } = valueFilters.value;
  void _dropped;
  valueFilters.value = rest;
}

/** The options to show: the whole list, or the part of it whose label or code matches the typing. */
function valueOptions(index: number): CatalogueEntry[] {
  const row = rows.value[index];
  if (!row) return [];
  const all = listFor(row.code);
  const needle = valueFilterFor(index).trim().toLowerCase();
  if (!needle) return all;
  return all.filter(
    (option) =>
      option.label.toLowerCase().includes(needle) || option.code.toLowerCase().includes(needle),
  );
}

/**
 * The text the dropdown may offer to add, or null. Nothing is offered without `createValue`, for
 * a session that cannot write, for an empty string, or for a value the list already has — by label
 * or by code, which are the same thing to the person typing.
 */
function addCandidate(index: number): string | null {
  if (props.readonly || props.createValue === undefined) return null;
  const row = rows.value[index];
  if (!row) return null;
  const typed = valueFilterFor(index).trim();
  if (!typed) return null;
  const wanted = typed.toLowerCase();
  const existing = listFor(row.code).some(
    (option) =>
      option.label.trim().toLowerCase() === wanted || option.code.toLowerCase() === wanted,
  );
  return existing ? null : typed;
}

/**
 * Adds the typed label to Configuration and, only when that worked, to the row. A failure leaves
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

  let created: CatalogueEntry | null = null;
  try {
    created = await create(row.code, typed);
  } catch {
    // A rejected promise is the same outcome as `null`: nothing was written, `created` stays null.
  } finally {
    creatingIndex.value = null;
  }

  if (created) {
    // The row keeps the code the server assigned; the label that was typed is not the identity.
    row.value = created.code;
    clearValueFilter(index);
    valueSelects.get(index)?.hidePopup?.();
    return;
  }

  row.value = previous;
  createError.value = {
    index,
    message: `No se pudo añadir «${typed}» a «${attributeLabel(row)}».`,
  };
  clearValueFilter(index);
  // The typed text is not the value of the row: put the previous one, as a label, back in the field.
  restoreValueInput(index, valueLabelOf(row, previous));
}

function setAttributeCode(index: number, code: string) {
  const row = rows.value[index];
  if (!row) return;
  row.code = code;
  clearValueFilter(index);
  // A value chosen from another attribute's list no longer makes sense.
  if (!listFor(code).some((option) => option.code === row.value)) row.value = '';
}

/** The select emits the stored code; the label it showed is the catalogue's business. */
function setValue(row: Attributes, code: string) {
  row.value = code;
}

/** Adds grouping to whole numbers while the field is not focused (10,000,000). */
function display(row: Attributes, index: number): string {
  const value = row.value ?? '';
  if (focusedIndex.value === index) return value;
  return /^-?\d+$/.test(value.trim()) && value.trim().length > 3
    ? Number(value).toLocaleString(UI_LOCALE)
    : value;
}

function onValue(row: Attributes, index: number, value: string | number | null) {
  void index;
  const text = value == null ? '' : String(value);
  // Only a numeric field is scrubbed: a string like SN-0042 must survive as typed.
  row.value = isNumeric(row) ? text.replace(/(?!^-)[^\d.,-]/g, '') : text;
}

function add() {
  const [next] = availableAttributes.value;
  if (next) rows.value.push({ code: next.code, value: '' });
}

/** Starts a row with an empty key: the user types the name the catalogue does not have. */
function addCustom() {
  const row: Attributes = { code: '', value: '' };
  rows.value.push(row);
}

/** A hand-typed key keeps the shape a JSON key needs: letters, digits, spaces, dash, underscore. */
function setCustomCode(index: number, value: string | number | null) {
  const row = rows.value[index];
  if (!row) return;
  row.code = (value == null ? '' : String(value)).replace(/[^A-Za-z0-9 _-]/g, '');
}

function remove(index: number) {
  rows.value.splice(index, 1);
  // Filters and errors are keyed by row index: after a removal none of them point at their row.
  valueFilters.value = {};
  valueSelects.clear();
  createError.value = null;
}

/**
 * The data type of a row: from Configuration for a listed attribute, Text for one typed by hand.
 * These are the values of `Constants.DataType` — an identity the API stores, not a label: the
 * select in Configuration shows «Importe» and the value here is still `Money`.
 */
function typeOf(row: Attributes): string {
  return props.types?.[row.code] ?? 'Text';
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
  color: var(--imc-text-muted);
}

.attribute-list {
  display: grid;
  row-gap: 2px;
}

.attribute-row {
  border-bottom: 1px solid var(--imc-hairline);
  min-height: 44px;
}

.attribute-row :deep(.q-field__native),
.attribute-row :deep(.q-field__input) {
  font-size: 15px;
}

.attribute-name :deep(.q-field__native) {
  color: var(--imc-text-body);
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
  color: var(--imc-danger-text);
  padding: 2px 0 4px;
}

/* The "add to Configuration" row reads as an action, not as another value of the list. */
.attribute-add-option {
  color: var(--q-primary);
}

.attribute-name--custom :deep(.q-field__native) {
  font-style: italic;
  color: var(--imc-text-body);
}

.attribute-name__hint {
  color: var(--imc-text-soft);
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
