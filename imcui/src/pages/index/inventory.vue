<template>
  <q-page>
    <q-linear-progress v-if="loading" indeterminate color="primary" />

    <q-splitter v-model="splitterModel" v-if="$q.screen.gt.sm">
      <template v-slot:before>
        <div class="q-pa-md">
          <div v-if="!loading && tree.length <= 1" class="text-grey-7 q-mb-md">
            No items yet — use the "Add New" node to create the first one.
          </div>
          <q-tree
            :key="treeVersion"
            :nodes="tree"
            node-key="id"
            selected-color="primary"
            v-model:selected="selected"
            @update:selected="(target) => selectData(target)"
            v-model:expanded="expanded"
          >
            <template v-slot:default-header="prop">
              <div class="row items-center">
                <q-icon :name="prop.node.icon" />
                <div>
                  <span>{{ prop.node.id }} - {{ prop.node.label }}</span>
                </div>
              </div>
            </template>
          </q-tree>
        </div>
      </template>

      <template v-slot:after>
        <q-tab-panels
          v-model="selected"
          animated
          transition-prev="jump-up"
          transition-next="jump-up"
        >
          <q-tab-panel v-for="item in panels" :key="item.id" :name="item.id">
            <div class="text-h4 q-mb-md">
              Id: {{ item.id }}
              <q-btn
                v-if="item.id > 0"
                label="Delete"
                type="button"
                color="negative"
                @click="deleteTreeItem"
              />
            </div>
            <q-form @submit="sendTreeItem" @reset="resetTreeItem" class="q-gutter-md">
              <q-input label="label" v-model="treeItem.label" :rules="[labelRule]" />
              <!-- <q-input label="icon" v-model="treeItem.icon" /> -->
              <q-select
                label="parentId"
                class="col-5"
                v-model="treeItem.parentId"
                :options="createSelectOptions(tree, item.id)"
                :option-label="
                  (opt) =>
                    Object(opt) === opt && 'label' in opt && opt.label == 'Add New'
                      ? 'No Parent'
                      : opt.label
                "
                :option-value="
                  (opt) =>
                    Object(opt) === opt && 'value' in opt && opt.value == 0 ? null : opt.value
                "
                map-options
                emit-value
              />
              <!-- <q-input label="parentId" v-model="treeItem.parentId" /> -->
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
                  @click="deleteAtttribute(index)"
                />
              </div>
              <!-- <div class="row q-gutter-sm">
                <div class="col-12">Attributes:</div>
                <div class="col-12">{{ treeItem.attributes }}</div>
              </div> -->
              <!-- <q-input label="attributes" v-model="treeItem.attributes" type="textarea" /> -->

              <!-- Add Attachments -->
              <q-btn
                label="Add Attachment"
                type="button"
                color="secondary"
                @click="addAttachment(item.id)"
              />
              <div v-for="(attachmentData, index) in attachments" :key="attachmentData.id">
                <div>
                  <q-btn
                    :label="
                      'Delete Attachment' + (attachmentData.id > 0 ? ` ${attachmentData.id}` : '')
                    "
                    type="button"
                    color="negative"
                    @click="deleteAttachment(index)"
                  /><br />
                  <div v-if="attachmentData.id == 0">
                    <q-file
                      v-model="file"
                      label="Select a file"
                      @update:model-value="(file) => onFileSelected(file, index)"
                    >
                      <template v-slot:prepend>
                        <q-icon name="attach_file" />
                      </template>
                    </q-file>
                    <q-select
                      label="Type"
                      v-model="attachmentData.type"
                      :options="attachmentTypes"
                    />
                  </div>
                  <br />
                  <span>Id: {{ attachmentData.id }}</span
                  ><br />
                  <!-- <span>ItemId: {{ attachmentData.itemId }}</span><br /> -->
                  <span>Type: {{ attachmentData.type }}</span
                  ><br />
                  <div v-if="attachmentData.type == 'Photo' && attachmentData.fileBase64 != null">
                    <q-img :src="attachmentData.fileBase64" height="75px" fit="contain" />
                  </div>
                </div>
              </div>
              <div>
                <q-btn label="Submit" type="submit" color="primary" />
                <q-btn label="Reset" type="reset" color="primary" flat class="q-ml-sm" />
              </div>
            </q-form>
          </q-tab-panel>
        </q-tab-panels>
      </template>
    </q-splitter>

    <div v-else class="q-pa-md">
      <div v-if="!loading && tree.length <= 1" class="text-grey-7 q-mb-md">
        No items yet — use the "Add New" node to create the first one.
      </div>
      <q-tree
        :key="treeVersion"
        :nodes="tree"
        node-key="id"
        selected-color="primary"
        v-model:selected="selected"
        @update:selected="(target) => selectData(target)"
        class="text-h5 text-weight-light"
        v-model:expanded="expanded"
      >
        <template v-slot:default-header="prop">
          <div class="row items-center">
            <!-- <q-icon :name="prop.node.icon" size="xl" /> -->
            <div>
              <span>{{ prop.node.id }} - {{ prop.node.label }}</span>
            </div>
          </div>
        </template>
      </q-tree>
    </div>

    <q-dialog
      v-model="showMobileDialog"
      :maximized="$q.screen.lt.md"
      transition-show="slide-up"
      transition-hide="slide-down"
    >
      <q-card :style="$q.screen.lt.md ? '' : 'min-width: 400px'">
        <q-card-section v-if="treeItem">
          <div class="text-h4 q-mb-md">Id: {{ treeItem.id }}</div>
          <q-form @submit="submitDialog" @reset="resetTreeItem" class="q-gutter-md">
            <q-input label="label" v-model="treeItem.label" :rules="[labelRule]" />
            <!-- <q-input label="icon" v-model="treeItem.icon" /> -->
            <q-select
              label="parentId"
              class="col-5"
              v-model="treeItem.parentId"
              :options="createSelectOptions(tree, treeItem.id)"
              :option-label="
                (opt) =>
                  Object(opt) === opt && 'label' in opt && opt.label == 'Add New'
                    ? 'No Parent'
                    : opt.label
              "
              :option-value="
                (opt) =>
                  Object(opt) === opt && 'value' in opt && opt.value == 0 ? null : opt.value
              "
              map-options
              emit-value
            />
            <!-- <q-input label="parentId" v-model="treeItem.parentId" /> -->
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
                @click="deleteAtttribute(index)"
              />
            </div>
            <!-- <div class="row q-gutter-sm">
              <div class="col-12">Attributes:</div>
              <div class="col-12">{{ treeItem.attributes }}</div>
            </div> -->
            <!-- <q-input label="attributes" v-model="treeItem.attributes" type="textarea" /> -->

            <!-- Add Attachments -->
            <q-btn
              label="Add Attachment"
              type="button"
              color="secondary"
              @click="addAttachment(treeItem.id)"
            />
            <div v-for="(attachmentData, index) in attachments" :key="attachmentData.id">
              <div>
                <q-btn
                  :label="
                    'Delete Attachment' + (attachmentData.id > 0 ? ` ${attachmentData.id}` : '')
                  "
                  type="button"
                  color="negative"
                  @click="deleteAttachment(index)"
                /><br />
                <div v-if="attachmentData.id == 0">
                  <q-file
                    v-model="file"
                    label="Select a file"
                    @update:model-value="(file) => onFileSelected(file, index)"
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
                <!-- <span>ItemId: {{ attachmentData.itemId }}</span><br /> -->
                <span>Type: {{ attachmentData.type }}</span
                ><br />
                <div v-if="attachmentData.type == 'Photo' && attachmentData.fileBase64 != null">
                  <q-img :src="attachmentData.fileBase64" height="75px" fit="contain" />
                </div>
              </div>
            </div>
            <q-card-actions align="between" class="q-px-none q-mt-md">
              <q-btn flat label="Cancel" color="negative" type="button" v-close-popup />
              <q-btn
                v-if="treeItem.id > 0"
                flat
                label="Delete"
                color="negative"
                type="button"
                @click="deleteTreeItem"
              />
              <q-btn label="Reset" color="secondary" flat type="reset" />
              <q-btn label="Submit" color="primary" type="submit" :loading="saving" />
            </q-card-actions>
          </q-form>
        </q-card-section>
      </q-card>
    </q-dialog>
  </q-page>
</template>

<script setup lang="ts">
import { ref, onMounted, watch } from 'vue';
import { useQuasar } from 'quasar';
import { itemsService } from '@services/items';
import { attachmentsService } from '@services/attachment';
import { constantsService } from '@services/constants';
import { fileToBase64 } from '@services/utilities';
import type { Item, Attributes } from '@services/items';
import type { Attachment } from '@services/attachment';
import type { Constants } from '@services/constants';
import { useFeedback } from '../../composables/useFeedback';

const feedback = useFeedback();
const $q = useQuasar();

const labelRule = (value: string) => (value && value.trim().length > 0) || 'Label is required';

const createItem = {
  id: 0,
  label: 'Add New',
  icon: '',
  children: [],
  parentId: null,
  attributes: '',
  attachments: [],
  parent: undefined,
};

const newAttachment = {
  id: 0,
  itemId: 0,
  type: null,
  fileName: null,
  filePath: null,
  fileBase64: null,
  attributes: null,
};

const newAttribute = {
  label: '',
  value: '',
};

const attachmentTypes = ref<string[]>([]);

const attributeLabels = ref<string[]>([]);

const attachments = ref<Attachment[]>([]);

const treeItem = ref<Item>({
  id: 0,
  label: '',
  icon: '',
  children: [],
  parentId: null,
  attributes: '',
  attachments: [],
  parent: undefined,
});

const splitterModel = ref(50);
const selected = ref(-1);
const tree = ref<Item[]>([]);

const panels = ref<Item[]>([]);

const fileBase64 = ref<string>('');
const file = ref<File>();
const loading = ref(false);
const saving = ref(false);
const treeVersion = ref(0);

const expanded = ref<number[]>([]);

function collectExpandable(nodes: { id: number; children?: unknown[] }[], acc: number[] = []): number[] {
  for (const node of nodes) {
    const kids = (node.children ?? []) as { id: number; children?: unknown[] }[];
    if (kids.length > 0) {
      acc.push(node.id);
      collectExpandable(kids, acc);
    }
  }
  return acc;
}

const attributes = ref<Attributes[]>([]);

const constantsData = ref<Constants[]>([]);

const showMobileDialog = ref(false);

async function loadData() {
  loading.value = true;

  try {
    const [items, constantsAttachmentTypes, constantsAttributeLabels, constants] =
      await Promise.all([
        itemsService.getAll(),
        constantsService.getByGroup('AttachmentType'),
        constantsService.getByGroup('Attribute'),
        constantsService.getAll(),
      ]);

    if (items != null && items != undefined) {
      if (items.tree != null && items.tree != undefined) {
        tree.value = items.tree;
        tree.value.unshift(createItem);
      }
      if (items.panels != null && items.panels != undefined) {
        panels.value = items.panels;
        panels.value.unshift(createItem);
      }
    }
    if (constantsAttachmentTypes != null && constantsAttachmentTypes != undefined) {
      attachmentTypes.value = constantsAttachmentTypes.map((x) => x.label);
    }
    if (constantsAttributeLabels != null && constantsAttributeLabels != undefined) {
      attributeLabels.value = constantsAttributeLabels.map((x) => x.label);
    }
    if (constants != null && constants != undefined) {
      constantsData.value = constants.panels;
    }
  } catch (error) {
    feedback.error(error, 'Could not load the items');
  } finally {
    loading.value = false;
    expanded.value = collectExpandable(tree.value);
    // Re-create the tree with data present so expansion and node state start consistent.
    treeVersion.value += 1;
  }
}

onMounted(() => {
  void loadData();
});

async function loadAttachments(id: number) {
  loading.value = true;

  try {
    const [attachmentResponse] = await Promise.all([attachmentsService.getAllByItemId(id)]);

    if (
      attachmentResponse != null &&
      attachmentResponse != undefined &&
      attachmentResponse.length > 0
    ) {
      attachments.value = attachmentResponse;
    } else {
      attachments.value = [];
    }
  } catch (error) {
    feedback.error(error, 'Could not load the attachments');
  } finally {
    loading.value = false;
  }
}

async function selectData(target: number) {
  const data = findItemById(tree.value, target);
  if (data != undefined) {
    treeItem.value.id = data.id;
    treeItem.value.label = data.label;
    treeItem.value.icon = data.icon;
    treeItem.value.children = data.children;
    treeItem.value.attributes = data.attributes;
    treeItem.value.attachments = data.attachments;
    treeItem.value.parentId = data.parentId;
    treeItem.value.parent = data.parent;
    if (data.id > 0) {
      const parsed = parseAttributes(data.attributes);
      attributes.value = attributeLabels.value.map((label) => ({
        label,
        value: parsed[label] ?? '',
      }));
      await loadAttachments(data.id);
    } else {
      const attributeLabelsData = attributeLabels.value.map((x) => ({
        label: x,
        value: '',
      }));
      attributes.value = attributeLabelsData;
      attachments.value = [];
    }
  }

  if ($q.screen.lt.md) {
    showMobileDialog.value = true;
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

function findItemById(item: Item[], id: number): Item | undefined {
  for (const data of item) {
    if (data.id === id) {
      return data;
    }

    const found = findItemById(data.children, id);

    if (found) {
      return found;
    }
  }

  return undefined;
}

async function sendTreeItem(): Promise<boolean> {
  const id = treeItem.value.id;
  saving.value = true;
  try {
    // Only attachments created in this form are sent; existing ones live in their own table.
    treeItem.value.attachments = attachments.value.filter((x) => x.id == 0);
    if (id > 0) {
      await itemsService.update(id, treeItem.value);
      feedback.success('Item updated');
    } else {
      await itemsService.create(treeItem.value);
      feedback.success('Item created');
    }
    resetTreeItem();
    await loadData();
    return true;
  } catch (error) {
    feedback.error(error, 'Could not save the item');
    return false;
  } finally {
    saving.value = false;
  }
}

async function submitDialog() {
  if (await sendTreeItem()) {
    showMobileDialog.value = false;
  }
}

function resetTreeItem() {
  selected.value = 0;
  const attributeLabelsData = attributeLabels.value.map((x) => ({
    label: x,
    value: '',
  }));

  treeItem.value = {
    id: 0,
    label: '',
    icon: '',
    children: [],
    parentId: null,
    attributes: '{}',
    attachments: [],
    parent: undefined,
  };

  fileBase64.value = '';
  attributes.value = attributeLabelsData;
  attachments.value = [];
  file.value = new File([''], '');
}

async function deleteTreeItem() {
  const id = treeItem.value.id;
  if (id <= 0) return;
  const confirmed = await feedback.confirmDelete(
    `Delete "${treeItem.value.label || id}"? Its attachments are deleted with it.`,
  );
  if (!confirmed) return;
  try {
    await itemsService.delete(id);
    feedback.success('Item deleted');
    resetTreeItem();
    await loadData();
  } catch (error) {
    feedback.error(error, 'Could not delete the item');
  }
}

function addAttachment(id: number) {
  attachments.value.push({
    id: newAttachment.id,
    itemId: id,
    type: newAttachment.type,
    fileName: newAttachment.fileName,
    filePath: newAttachment.filePath,
    fileBase64: newAttachment.fileBase64,
    attributes: newAttachment.attributes,
  });
}

function addAttribute() {
  attributes.value.push({
    label: newAttribute.label,
    value: newAttribute.value,
  });
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
  } else {
    attachments.value.splice(index, 1);
  }
}

function deleteAtttribute(index: number) {
  const attributeData = attributes.value[index];
  if (attributeData != null && attributeData != undefined) {
    attributes.value.splice(index, 1);
  }
}

async function onFileSelected(file: File | null, index: number) {
  if (!file) {
    if (attachments.value[index] != undefined) {
      attachments.value[index].fileName = '';
      attachments.value[index].fileBase64 = '';
    }
    return;
  }

  try {
    fileBase64.value = await fileToBase64(file);
    if (attachments.value[index] != undefined) {
      attachments.value[index].fileName = file.name;
      attachments.value[index].fileBase64 = fileBase64.value;
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
  const constantsFiltered = constantsData.value.filter((x) => x.parentId == null);
  return constantsFiltered?.map((x) => x.group).includes(attribute);
}

function getAttributeList(attribute: string): string[] {
  const constantsFiltered = constantsData.value.filter(
    (x) => x.parentId != null && x.group == attribute,
  );
  let sortedAttributeList: string[];
  if (constantsFiltered.every((x) => x.sortOrder == 0)) {
    const attributeList = constantsFiltered?.map((x) => x.label);
    sortedAttributeList = attributeList.sort((a, b) => a.localeCompare(b));
  } else {
    const attributeList = constantsFiltered?.sort((a, b) => a.sortOrder - b.sortOrder);
    sortedAttributeList = attributeList?.map((x) => x.label);
  }
  return sortedAttributeList;
}

function validateAttributeValue(value: string): boolean | string {
  return (value ?? '').length <= 500 || 'Max 500 characters';
}

function createSelectOptions(items: Item[], id: number) {
  const flattened = itemsService.flattenItems(items);

  const mappedFlattened = flattened
    .filter((x) => x.id != id || id == 0)
    .map((item) => ({
      value: item.id,
      label: item.hierarchicalLabel,
      // Optional: add indentation for better visual hierarchy
      displayLabel: `${'  '.repeat(item.depth)}${item.label}`,
      depth: item.depth,
    }));
  return mappedFlattened;
}
</script>
