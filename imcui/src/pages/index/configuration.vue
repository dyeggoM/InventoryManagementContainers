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
    :readonly="!canWrite"
    entity-label="Constant"
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
          v-if="treeItem.id > 0 && canWrite"
          flat
          no-caps
          icon="add"
          label="Add child constant"
          color="primary"
          class="q-mb-xs"
          @click="createChildEntry"
        />
      </div>
      <q-input label="Name" v-model="treeItem.label" :readonly="!canWrite" :rules="[labelRule]" />
      <q-select
        label="Parent"
        v-model="treeItem.parentId"
        :options="parentOptions()"
        :display-value="treeItem.parentId == null ? 'No parent' : undefined"
        :readonly="!canWrite"
        map-options
        emit-value
        class="q-mt-sm"
      />
    </div>

    <div class="imc-section">
      <div class="imc-section__title">Definition</div>
      <q-input label="Group" v-model="treeItem.group" :readonly="!canWrite" />
      <q-input
        dense
        label="Description"
        v-model="treeItem.description"
        :readonly="!canWrite"
        type="textarea"
        autogrow
        class="q-mt-md"
      />
      <q-select
        v-if="isAttributeDefinition"
        dense
        label="Data type"
        v-model="treeItem.dataType"
        :options="dataTypes"
        :hint="typeHint"
        :readonly="!canWrite"
        class="q-mt-md"
      />
      <q-input label="Sort order" v-model="treeItem.sortOrder" :readonly="!canWrite" class="q-mt-md" />
    </div>
  </tree-master-detail>
</template>

<script setup lang="ts">
import { computed, onMounted } from 'vue';
import TreeMasterDetail from '../../components/TreeMasterDetail.vue';
import { useTreeEditor } from '../../composables/useTreeEditor';
import { useAuthStore } from '../../stores/auth-store';
import { constantsService, type Constants } from '@services/constants';

const auth = useAuthStore();
// Configuration is an admin page; the guard already enforces it, this keeps the form honest for a
// superadmin who is impersonating somebody.
const canWrite = computed(() => auth.canWrite);

const blank = (): Constants => ({
  id: 0,
  label: '',
  icon: '',
  children: [],
  sortOrder: 0,
  group: '',
  parentId: null,
  description: '',
  parent: undefined,
});

const dataTypes = ['Text', 'Number', 'Money', 'Date', 'LongText'];

/** Only a child of the Attribute group describes an item field, so only it has a data type. */
const isAttributeDefinition = computed(
  () => treeItem.value.group === 'Attribute' && (treeItem.value.parentId ?? 0) > 0,
);

const typeHint = computed(() => {
  switch (treeItem.value.dataType) {
    case 'Money':
      return 'Whole numbers, shown with separators and a decimal keypad on phones.';
    case 'Number':
      return 'Digits and one decimal separator.';
    case 'Date':
      return 'Picked from the calendar instead of typed.';
    case 'LongText':
      return 'A paragraph, edited in a text box.';
    default:
      return 'Any text, exactly as typed.';
  }
});

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
} = useTreeEditor<Constants>({
  service: constantsService,
  addNew: () => ({ ...blank(), label: 'Add New' }),
  blank,
  flatten: (nodes) => constantsService.flattenConstants(nodes),
  entity: 'constant',
  emptyMessage: 'No constants yet',
  deleteHint: 'Its children are deleted with it.',
  splitter: 30,
  // Only the entry: a constant is a name, an icon and a parent.
  dirtySnapshot: (entry) => JSON.stringify(entry),
});

onMounted(() => {
  void loadData();
});
</script>