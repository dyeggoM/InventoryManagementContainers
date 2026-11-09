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
    @select="selectData"
    @submit="submitDialog"
    @reset="resetTreeItem"
    @delete="deleteTreeItem"
  >
    <q-input label="label" v-model="treeItem.label" :rules="[labelRule]" />
    <q-select
      label="parentId"
      v-model="treeItem.parentId"
      :options="parentOptions()"
      map-options
      emit-value
    />

    <q-btn label="Add Attribute" type="button" color="secondary" @click="addAttribute" />
    <div
      class="row q-gutter-sm"
      v-for="(attribute, index) in attributes"
      :key="`attribute-${index}`"
    >
      <q-input
        class="col-5"
        label="label"
        v-model="attribute.label"
        :rules="[validateAttributeValue]"
      />

      <q-input
        v-if="
          !checkAttributeHasList(attribute.label) &&
          attribute.label != 'Description' &&
          attribute.label != 'Notes'
        "
        class="col-5"
        label="value"
        v-model="attribute.value"
        :rules="[validateAttributeValue]"
      />
      <q-input
        v-else-if="
          !checkAttributeHasList(attribute.label) &&
          (attribute.label == 'Description' || attribute.label == 'Notes')
        "
        class="col-5"
        label="value"
        v-model="attribute.value"
        type="textarea"
        :rules="[validateAttributeValue]"
      />
      <q-select
        v-else-if="checkAttributeHasList(attribute.label)"
        label="value"
        class="col-5"
        v-model="attribute.value"
        :options="getAttributeList(attribute.label)"
      />
      <q-btn
        label="-"
        type="button"
        color="negative"
        class="col-1"
        @click="deleteAttribute(index)"
      />
    </div>

    <q-btn
      label="Add Attachment"
      type="button"
      color="secondary"
      @click="addAttachment(treeItem.id)"
    />
    <div v-for="(attachmentData, index) in attachments" :key="attachmentData.id">
      <div>
        <q-btn
          :label="'Delete Attachment' + (attachmentData.id > 0 ? ` ${attachmentData.id}` : '')"
          type="button"
          color="negative"
          @click="deleteAttachment(index)"
        /><br />
        <div v-if="attachmentData.id == 0">
          <q-file
            v-model="file"
            label="Select a file"
            @update:model-value="(selected) => onFileSelected(selected, index)"
          >
            <template v-slot:prepend>
              <q-icon name="attach_file" />
            </template>
          </q-file>
          <q-select label="Type" v-model="attachmentData.type" :options="attachmentTypes" />
        </div>
        <br />
        <span>Id: {{ attachmentData.id }}</span
        ><br />
        <span>Type: {{ attachmentData.type }}</span
        ><br />
        <div v-if="attachmentData.type == 'Photo' && attachmentData.fileBase64 != null">
          <q-img :src="attachmentData.fileBase64" height="75px" fit="contain" />
        </div>
      </div>
    </div>
  </tree-master-detail>
</template>

<script setup lang="ts">
import { onMounted, ref, watch } from 'vue';
import TreeMasterDetail from '../../components/TreeMasterDetail.vue';
import { useTreeEditor } from '../../composables/useTreeEditor';
import { useFeedback } from '../../composables/useFeedback';
import { itemsService, type Item, type Attributes } from '@services/items';
import { attachmentsService, type Attachment } from '@services/attachment';
import { constantsService, type Constants } from '@services/constants';
import { fileToBase64 } from '@services/utilities';

const feedback = useFeedback();

const attributes = ref<Attributes[]>([]);
const attachments = ref<Attachment[]>([]);
const attachmentTypes = ref<string[]>([]);
const attributeLabels = ref<string[]>([]);
const constantsData = ref<Constants[]>([]);
const fileBase64 = ref('');
const file = ref<File>();

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

const newAttachment = (id: number): Attachment => ({
  id: 0,
  itemId: id,
  type: null,
  fileName: null,
  filePath: null,
  fileBase64: null,
  attributes: null,
});

function blankAttributes(): Attributes[] {
  return attributeLabels.value.map((label) => ({ label, value: '' }));
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
  emptyMessage,
  labelRule,
  loadData,
  selectData,
  resetTreeItem,
  submitDialog,
  deleteTreeItem,
  parentOptions,
} = useTreeEditor<Item>({
  service: itemsService,
  addNew: () => ({ ...blank(), label: 'Add New' }),
  blank,
  flatten: (nodes) => itemsService.flattenItems(nodes),
  entity: 'item',
  emptyMessage: 'No items yet — use the "Add New" node to create the first one.',
  deleteHint: 'Its attachments are deleted with it.',
  splitter: 50,
  onSelect: async (entry) => {
    if (entry.id > 0) {
      const parsed = parseAttributes(entry.attributes);
      attributes.value = attributeLabels.value.map((label) => ({
        label,
        value: parsed[label] ?? '',
      }));
      await loadAttachments(entry.id);
    } else {
      attributes.value = blankAttributes();
      attachments.value = [];
    }
  },
  onReset: () => {
    attributes.value = blankAttributes();
    attachments.value = [];
    fileBase64.value = '';
    file.value = undefined;
  },
  beforeSave: (entry) => {
    // Only attachments created in this form are sent; existing ones live in their own table.
    entry.attachments = attachments.value.filter((attachment) => attachment.id == 0);
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
    attributeLabels.value = (attributeRows ?? []).map((row) => row.label);
    constantsData.value = all?.panels ?? [];
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
    const rows = await attachmentsService.getAllByItemId(id);
    attachments.value = rows ?? [];
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

function addAttribute() {
  attributes.value.push({ label: '', value: '' });
}

function deleteAttribute(index: number) {
  attributes.value.splice(index, 1);
}

function addAttachment(id: number) {
  attachments.value.push(newAttachment(id));
}

async function deleteAttachment(index: number) {
  const attachmentData = attachments.value[index];
  if (!attachmentData) return;
  if (attachmentData.id > 0) {
    const confirmed = await feedback.confirmDelete(
      `Delete attachment "${attachmentData.fileName || attachmentData.id}"?`,
    );
    if (!confirmed) return;
    try {
      await attachmentsService.delete(attachmentData.id);
      feedback.success('Attachment deleted');
      await loadAttachments(attachmentData.itemId);
    } catch (error) {
      feedback.error(error, 'Could not delete the attachment');
    }
    return;
  }
  attachments.value.splice(index, 1);
}

async function onFileSelected(selected: File | null, index: number) {
  const target = attachments.value[index];
  if (!selected) {
    if (target) {
      target.fileName = '';
      target.fileBase64 = '';
    }
    return;
  }
  try {
    fileBase64.value = await fileToBase64(selected);
    if (target) {
      target.fileName = selected.name;
      target.fileBase64 = fileBase64.value;
    }
  } catch (error) {
    feedback.error(error, 'Could not read the selected file');
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

function checkAttributeHasList(attribute: string): boolean {
  return constantsData.value
    .filter((constant) => constant.parentId == null)
    .map((constant) => constant.group)
    .includes(attribute);
}

function getAttributeList(attribute: string): string[] {
  const matching = constantsData.value.filter(
    (constant) => constant.parentId != null && constant.group == attribute,
  );
  if (matching.every((constant) => constant.sortOrder == 0)) {
    return matching.map((constant) => constant.label).sort((a, b) => a.localeCompare(b));
  }
  return matching
    .slice()
    .sort((a, b) => a.sortOrder - b.sortOrder)
    .map((constant) => constant.label);
}

function validateAttributeValue(value: string): boolean | string {
  return (value ?? '').length <= 500 || 'Max 500 characters';
}
</script>
