<template>
  <tree-master-detail
    v-model:selected="selected"
    v-model:expanded="expanded"
    v-model:splitter="splitterModel"
    v-model:dialog="showMobileDialog"
    :tree="tree"
    :panels="panels"
    :entry-id="treeItem.id"
    :tree-version="treeVersion"
    :loading="loading"
    :saving="saving"
    :empty-message="emptyMessage"
    :dirty="isDirty"
    entity-label="Item"
    :entry-label="treeItem.label"
    @select="selectData"
    @create="createEntry"
    @submit="submitDialog"
    @reset="resetTreeItem"
    @delete="deleteTreeItem"
    @duplicate="duplicateEntry"
  >
    <div class="imc-section">
      <div class="row items-center justify-between q-mb-xs">
        <div class="imc-section__title q-mb-none">Details</div>
        <q-btn
          v-if="treeItem.id > 0"
          flat
          no-caps
          icon="add"
          label="Add child item"
          color="primary"
          class="q-mb-xs"
          @click="createChildEntry"
        />
      </div>
      <q-input label="Name" v-model="treeItem.label" :rules="[labelRule]" />
      <q-select
        label="Parent item"
        v-model="treeItem.parentId"
        :options="parentOptions()"
        :display-value="treeItem.parentId == null ? 'No parent' : undefined"
        map-options
        emit-value
        class="q-mt-sm"
      />
    </div>

    <div class="imc-section">
      <div class="imc-section__title">Attributes</div>
      <attribute-editor
        v-model="attributes"
        :labels="attributeLabels"
        :lists="attributeLists"
        :types="attributeTypes"
      />
    </div>

    <div class="imc-section">
      <div class="imc-section__title">Attachments</div>
      <attachment-editor
        v-model="attachments"
        :types="attachmentTypes"
        @add="addAttachment"
        @remove="removeAttachment"
      />
    </div>
  </tree-master-detail>
</template>

<script setup lang="ts">
import { onMounted, ref, watch } from 'vue';
import TreeMasterDetail from '../../components/TreeMasterDetail.vue';
import AttributeEditor from '../../components/AttributeEditor.vue';
import AttachmentEditor, { type AttachmentRow } from '../../components/AttachmentEditor.vue';
import { useTreeEditor } from '../../composables/useTreeEditor';
import { useFeedback } from '../../composables/useFeedback';
import { itemsService, type Item, type Attributes } from '@services/items';
import { attachmentsService } from '@services/attachment';
import { constantsService } from '@services/constants';

const feedback = useFeedback();

const attributes = ref<Attributes[]>([]);
const attributeLabels = ref<string[]>([]);
const attributeTypes = ref<Record<string, string>>({});
const attributeLists = ref<Record<string, string[]>>({});
const attachmentTypes = ref<string[]>([]);
const attachments = ref<AttachmentRow[]>([]);

const blank = (): Item => ({
  id: 0,
  label: '',
  icon: '',
  children: [],
  parentId: null,
  attributes: '{}',
  attachments: [],
  parent: undefined,
});

function blankAttributes(): Attributes[] {
  return attributeLabels.value.map((label) => ({ label, value: '' }));
}

/**
 * The rows an entry produces: every catalogue attribute, plus any key the record carries that the
 * catalogue does not define (a value typed on this item alone must survive a reload).
 */
function rowsFrom(raw: string | null | undefined): Attributes[] {
  const parsed = parseAttributes(raw);
  const rows = attributeLabels.value.map((label) => ({ label, value: parsed[label] ?? '' }));
  const extra = Object.keys(parsed).filter((key) => !attributeLabels.value.includes(key));
  return [...rows, ...extra.map((key) => ({ label: key, value: parsed[key] ?? '' }))];
}

const {
  tree,
  panels,
  treeItem,
  splitterModel,
  selected,
  expanded,
  loading,
  saving,
  treeVersion,
  showMobileDialog,
  isDirty,
  emptyMessage,
  labelRule,
  loadData,
  selectData,
  resetTreeItem,
  createEntry,
  createChildEntry,
  duplicateEntry,
  submitDialog,
  deleteTreeItem,
  parentOptions,
} = useTreeEditor<Item>({
  service: itemsService,
  addNew: () => ({ ...blank(), label: 'Add New' }),
  blank,
  flatten: (nodes) => itemsService.flattenItems(nodes),
  entity: 'item',
  emptyMessage: 'No items yet',
  deleteHint: 'Its attachments are deleted with it.',
  splitter: 34,
  // The entry alone does not describe the form: the attributes and the attachment rows live here.
  dirtySnapshot: (entry) =>
    JSON.stringify({
      entry,
      attributes: attributes.value,
      attachments: attachments.value.map((row) => [
        row.id,
        row.type ?? '',
        row.fileName ?? '',
        (row.uploadBase64 ?? '').length,
      ]),
    }),
  onSelect: async (entry) => {
    if (entry.id > 0) {
      attributes.value = rowsFrom(entry.attributes);
      await loadAttachments(entry.id);
    } else {
      attributes.value = blankAttributes();
      attachments.value = [];
    }
  },
  onDuplicate: (source) => {
    attributes.value = rowsFrom(source.attributes);
    // Files are not copied: the new record points at nothing until it is given its own.
    attachments.value = [];
  },
  onReset: () => {
    attributes.value = blankAttributes();
    attachments.value = [];
  },
  validate: () => {
    if (attributes.value.some((row) => !row.label.trim() && row.value.trim())) {
      return 'Give every attribute a name';
    }
    const keys = attributes.value.map((row) => row.label.trim().toLowerCase()).filter(Boolean);
    if (new Set(keys).size !== keys.length) return 'Two attributes have the same name';
    for (const attachment of attachments.value.filter((row) => row.id === 0)) {
      if (!attachment.fileName) return 'Every new attachment needs a file';
      if (!attachment.type) return 'Every new attachment needs a type';
    }
    return null;
  },
  beforeSave: (entry) => {
    // Only attachments created in this form are sent; existing ones live in their own table.
    entry.attachments = attachments.value
      .filter((row) => row.id === 0)
      .map((row) => ({
        id: 0,
        itemId: row.itemId,
        type: row.type ?? null,
        fileName: row.fileName ?? null,
        // The API writes these bytes to disk and returns a compressed preview.
        uploadBase64: row.uploadBase64 ?? null,
        attributes: row.attributes ?? null,
      }));
  },
});

async function loadConstants() {
  try {
    const [attachmentTypeRows, attributeRows, all] = await Promise.all([
      constantsService.getByGroup('AttachmentType'),
      constantsService.getByGroup('Attribute'),
      constantsService.getAll(),
    ]);

    attachmentTypes.value = (attachmentTypeRows ?? []).map((row) => row.label);
    attributeLabels.value = (attributeRows ?? [])
      .slice()
      .sort((a, b) => a.sortOrder - b.sortOrder)
      .map((row) => row.label);

    // How each attribute is edited comes from its definition in Configuration.
    const types: Record<string, string> = {};
    for (const row of attributeRows ?? []) types[row.label] = row.dataType ?? 'Text';
    attributeTypes.value = types;

    // A label with children in the constants tree is a choice list; anything else is free text.
    const lists: Record<string, string[]> = {};
    for (const constant of all?.panels ?? []) {
      if (!constant.group || constant.parentId == null) continue;
      const bucket = lists[constant.group] ?? [];
      bucket.push(constant.label);
      lists[constant.group] = bucket;
    }
    for (const group of Object.keys(lists)) {
      lists[group] = (lists[group] ?? []).sort((a, b) => a.localeCompare(b));
    }
    attributeLists.value = lists;
    attributes.value = blankAttributes();
  } catch (error) {
    feedback.error(error, 'Could not load the constants');
  }
}

onMounted(async () => {
  // Constants first: the attribute form is built from them, and selecting a node needs them.
  await loadConstants();
  await loadData();
});

async function loadAttachments(id: number) {
  try {
    attachments.value = (await attachmentsService.getAllByItemId(id)) ?? [];
  } catch (error) {
    feedback.error(error, 'Could not load the attachments');
  }
}

function parseAttributes(raw: string | null | undefined): Record<string, string> {
  if (!raw) return {};
  try {
    const parsed: unknown = JSON.parse(raw);
    return parsed && typeof parsed === 'object' ? (parsed as Record<string, string>) : {};
  } catch {
    // Rows written before the serializer existed can hold invalid JSON; the form must still open.
    feedback.error('This item has malformed attributes; they were reset in the form.');
    return {};
  }
}

function addAttachment() {
  attachments.value.push({
    id: 0,
    itemId: treeItem.value.id,
    // Photos are the common case on a phone, and the type decides which picker opens.
    type: attachmentTypes.value.includes('Photo') ? 'Photo' : null,
    fileName: null,
    uploadBase64: null,
    attributes: null,
  });
}

async function removeAttachment(index: number) {
  const row = attachments.value[index];
  if (!row) return;
  if (row.id === 0) {
    attachments.value.splice(index, 1);
    return;
  }
  const confirmed = await feedback.confirmDelete(
    `Delete attachment "${row.fileName || row.id}"? This cannot be undone.`,
  );
  if (!confirmed) return;
  try {
    await attachmentsService.delete(row.id);
    feedback.success('Attachment deleted');
    await loadAttachments(row.itemId);
  } catch (error) {
    feedback.error(error, 'Could not delete the attachment');
  }
}

watch(
  attributes,
  () => {
    // Build the payload with a serializer, never by string concatenation: a value containing a
    // quote, backslash or newline used to produce invalid JSON that the API stored verbatim.
    const record: Record<string, string> = {};
    for (const attribute of attributes.value) {
      if (attribute.label) record[attribute.label] = attribute.value ?? '';
    }
    treeItem.value.attributes = JSON.stringify(record);
  },
  { deep: true },
);
</script>