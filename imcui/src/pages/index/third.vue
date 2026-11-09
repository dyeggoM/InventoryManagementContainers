<template>
  <q-page>
    <q-splitter v-model="splitterModel" v-if="$q.screen.gt.sm">
      <template v-slot:before>
        <div class="q-pa-md">
          <q-tree
            :nodes="tree"
            node-key="id"
            selected-color="primary"
            v-model:selected="selected"
            @update:selected="(target) => selectData(target)"
            default-expand-all
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
                type="submit"
                color="negative"
                @click="deleteTreeItem"
              />
            </div>
            <q-form @submit="sendTreeItem" @reset="resetTreeItem" class="q-gutter-md">
              <q-input label="label" v-model="treeItem.label" />
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
      <q-tree
        :nodes="tree"
        node-key="id"
        selected-color="primary"
        v-model:selected="selected"
        @update:selected="(target) => selectData(target)"
        class="text-h5 text-weight-light"
        default-expand-all
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
          <q-form @submit="sendTreeItem" @reset="resetTreeItem" class="q-gutter-md">
            <q-input label="label" v-model="treeItem.label" />
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
          </q-form>
        </q-card-section>

        <q-card-actions align="between">
          <q-btn flat label="Cancel" color="negative" v-close-popup />
          <q-btn flat label="Reset" color="secondary" @click="resetTreeItem" />
          <q-btn flat label="Submit" color="primary" @click="sendTreeItem" v-close-popup />
        </q-card-actions>
      </q-card>
    </q-dialog>
  </q-page>
</template>

<script setup lang="ts">
import { ref, onMounted, watch } from 'vue';
import { itemsService } from '@services/items';
import { attachmentsService } from '@services/attachment';
import { constantsService } from '@services/constants';
import { fileToBase64 } from '@services/utilities';
import type { Item, Attributes } from '@services/items';
import type { Attachment } from '@services/attachment';
import type { Constants } from '@services/constants';

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
const tree = ref<Item[]>([
  {
    label: 'Relax Hotel',
    id: 0,
    icon: '',
    children: [
      {
        label: 'Food',
        id: 1,
        icon: 'restaurant_menu',
        children: [],
      },
    ],
  },
]);

const panels = ref<Item[]>([
  {
    label: 'Relax Hotel',
    id: 0,
    icon: '',
    children: [],
  },
  {
    label: 'Food',
    id: 1,
    icon: 'restaurant_menu',
    children: [],
  },
]);

const fileBase64 = ref<string>('');
const file = ref<File>();
const loading = ref(false);

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
    console.log(items);
    if (constantsAttachmentTypes != null && constantsAttachmentTypes != undefined) {
      attachmentTypes.value = constantsAttachmentTypes.map((x) => x.label);
    }
    console.log(constantsAttachmentTypes);
    if (constantsAttributeLabels != null && constantsAttributeLabels != undefined) {
      attributeLabels.value = constantsAttributeLabels.map((x) => x.label);
    }
    console.log(constantsAttributeLabels);
    if (constants != null && constants != undefined) {
      constantsData.value = constants.panels;
    }
    console.log(constants);
  } catch (error) {
    console.error(error);
  } finally {
    loading.value = false;
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
    console.error(error);
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
      const json = JSON.parse(data.attributes ?? '{}') as Record<string, string>;
      attributes.value = attributeLabels.value.map((label) => ({
        label,
        value: json[label] ?? '',
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

  if (window.innerWidth < 768) {
    showMobileDialog.value = true;
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

async function sendTreeItem() {
  const id = treeItem.value.id;
  if (id > 0) {
    //TODO: ADD ATTACHMENTS, ADD ONLY ATTACHMENTS WITH ID 0
    treeItem.value.attachments = [];
    const attachmentsData = attachments.value.filter((x) => x.id == 0);
    treeItem.value.attachments = attachmentsData;
    const [itemUpdate] = await Promise.all([itemsService.update(id, treeItem.value)]);
    console.log(itemUpdate);
    resetTreeItem();
    await Promise.all([loadData()]);
  } else if (id == 0) {
    //TODO: ADD ATTACHMENTS, ADD ONLY ATTACHMENTS WITH ID 0
    // treeItem.value.attachments=[]
    treeItem.value.attachments = [];
    const attachmentsData = attachments.value.filter((x) => x.id == 0);
    treeItem.value.attachments = attachmentsData;
    const [itemCreate] = await Promise.all([itemsService.create(treeItem.value)]);
    console.log(itemCreate);
    resetTreeItem();
    await Promise.all([loadData()]);
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
  if (id > 0) {
    await Promise.all([itemsService.delete(id)]);
    resetTreeItem();
    await Promise.all([loadData()]);
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
  if (attachmentData != null && attachmentData != undefined) {
    if (attachmentData.id > 0) {
      await Promise.all([attachmentsService.delete(attachmentData.id)]);

      await Promise.all([loadAttachments(attachmentData.itemId)]);
    } else {
      attachments.value.splice(index, 1);
    }
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
    console.error('Unable to convert file:', error);
  }
}

watch(
  attributes,
  () => {
    let json = '{';
    attributes.value.forEach((x) => {
      json += `"${x.label}":"${x.value}",`;
    });
    if (json.length > 1) {
      json = json.slice(0, -1);
    }
    json += '}';
    treeItem.value.attributes = json;
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
  return !value.includes('"') || 'Double-quotes are not allowed';
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
  console.log(mappedFlattened);
  return mappedFlattened;
}
</script>
