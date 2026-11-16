<template>
  <!--
    Two sections and no wrapper on purpose: the page's sections are styled as siblings
    (`.imc-section + .imc-section` in `app.scss` draws the divider), so a root element of our own
    would hide the one below the attribute block.
  -->
  <div class="imc-section">
    <div class="row items-center justify-between q-mb-xs">
      <div class="imc-section__title q-mb-none">{{ copy.details }}</div>
      <slot name="details-actions" />
    </div>
    <q-input
      :label="copy.name"
      v-model="label"
      :readonly="readonly"
      :rules="rules"
      :hint="copy.nameHint"
      class="item-editor__name"
    />
    <q-select
      :label="copy.parent"
      v-model="parent"
      :options="parentOptions"
      :display-value="parentIsEmpty ? copy.parentEmpty : undefined"
      :readonly="readonly"
      :hint="copy.parentHint"
      map-options
      emit-value
      :use-input="mode === 'group'"
      :new-value-mode="mode === 'group' ? 'add-unique' : undefined"
      class="q-mt-sm item-editor__parent"
    />
  </div>

  <div class="imc-section">
    <div class="imc-section__title">{{ copy.attributes }}</div>
    <attribute-editor
      v-model="attributes"
      :labels="attributeLabels"
      :lists="attributeLists"
      :types="attributeTypes"
      :readonly="readonly"
      :create-value="createValue"
    />
  </div>
</template>

<script lang="ts">
/**
 * What the parent field means.
 *
 * `tree` — the inventory: the value is the **id of another item**, chosen from the tree, and the
 * list is the tree's own flattened nodes. There is no free text: a parent that is not an item
 * cannot exist.
 *
 * `group` — the import: the value is the **name of the group** the row will be created under (the
 * correction field `subcategory`), a string, and typing a name the list does not have yet is
 * allowed because that names a new subcategory rather than pointing at another record.
 */
export type ItemEditorMode = 'tree' | 'group';

/** One entry of the parent list: an item id in `tree` mode, a group name in `group` mode. */
export type ItemEditorOption = { label: string; value: string | number | null };

/** The copy around the fields, so one form can speak the language of the screen that hosts it. */
export type ItemEditorCopy = {
  details: string;
  name: string;
  nameHint: string | undefined;
  parent: string;
  parentHint: string | undefined;
  parentEmpty: string;
  attributes: string;
};
</script>

<script setup lang="ts">
import { computed } from 'vue';
import AttributeEditor from './AttributeEditor.vue';
import type { Attributes } from '@services/items';

/**
 * The item form: its name, its parent and its attribute rows. It is what the inventory screen edits
 * and what the import's row correction edits, which is the point — a hand-created item and an
 * imported one must offer the same fields, or the import writes records the form cannot fix.
 *
 * Attachments stay in the page that owns the record: an import row has no files, so a component
 * that carried an attachment editor would carry one no caller uses.
 */

const props = withDefaults(
  defineProps<{
    /** See `ItemEditorMode`. Defaults to the inventory's own meaning. */
    mode?: ItemEditorMode;
    /** The entries the parent select offers, in the order they are shown. */
    parentOptions?: ItemEditorOption[];
    /** The name's validation, exactly the one the host screen applies. */
    labelRule?: (value: string) => boolean | string;
    /** Attribute labels from Configuration, in its order. */
    attributeLabels: string[];
    /** label -> allowed values. A label with a list is a choice control, the rest is free text. */
    attributeLists?: Record<string, string[]>;
    /** label -> how its value is edited (Text, Number, Money, Date, LongText). */
    attributeTypes?: Record<string, string | null | undefined>;
    /** The session cannot write: the fields render read-only and the row buttons are gone. */
    readonly?: boolean;
    /** Present only for an admin or a superadmin; the API enforces the same rule. */
    createValue?: ((label: string, value: string) => Promise<boolean>) | undefined;
    /** Fields the host wants worded differently; anything left out keeps the default below. */
    copy?: Partial<ItemEditorCopy>;
  }>(),
  {
    mode: 'tree',
    parentOptions: () => [],
    attributeLists: () => ({}),
    attributeTypes: () => ({}),
    readonly: false,
  },
);

const label = defineModel<string>('label', { required: true });
const parent = defineModel<string | number | null | undefined>('parent', { default: null });
const attributes = defineModel<Attributes[]>('attributes', { required: true });

const copy = computed<ItemEditorCopy>(() => ({
  details: props.copy?.details ?? 'Details',
  name: props.copy?.name ?? 'Name',
  nameHint: props.copy?.nameHint,
  parent: props.copy?.parent ?? 'Parent item',
  parentHint: props.copy?.parentHint,
  parentEmpty: props.copy?.parentEmpty ?? 'No parent',
  attributes: props.copy?.attributes ?? 'Attributes',
}));

/** The tree's sentinel is `null`; a group mode row with no group yet is the empty string. */
const parentIsEmpty = computed(
  () => parent.value === null || parent.value === undefined || parent.value === '',
);

/** An empty rule list is not the same as `[undefined]`: with no rule the field is simply free. */
const rules = computed(() => (props.labelRule ? [props.labelRule] : undefined));
</script>
