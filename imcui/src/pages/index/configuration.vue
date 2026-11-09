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
    entity-label="Constant"
    :entry-label="treeItem.label"
    @select="selectData"
    @create="createEntry"
    @submit="submitDialog"
    @reset="resetTreeItem"
    @delete="deleteTreeItem"
  >
    <div class="imc-section">
      <div class="row items-center justify-between q-mb-xs">
        <div class="imc-section__title q-mb-none">Details</div>
        <q-btn
          v-if="treeItem.id > 0"
          flat
          no-caps
          icon="add"
          label="Add child constant"
          color="primary"
          class="q-mb-xs"
          @click="createChildEntry"
        />
      </div>
      <q-input label="Name" v-model="treeItem.label" :rules="[labelRule]" />
      <q-select
        label="Parent"
        v-model="treeItem.parentId"
        :options="parentOptions()"
        :display-value="treeItem.parentId == null ? 'No parent' : undefined"
        map-options
        emit-value
        class="q-mt-sm"
      />
    </div>

    <div class="imc-section">
      <div class="imc-section__title">Definition</div>
      <q-input label="Group" v-model="treeItem.group" />
      <q-input
        dense
        label="Description"
        v-model="treeItem.description"
        type="textarea"
        autogrow
        class="q-mt-md"
      />
      <q-input label="Sort order" v-model="treeItem.sortOrder" class="q-mt-md" />
    </div>
  </tree-master-detail>
</template>

<script setup lang="ts">
import { onMounted } from 'vue';
import TreeMasterDetail from '../../components/TreeMasterDetail.vue';
import { useTreeEditor } from '../../composables/useTreeEditor';
import { constantsService, type Constants } from '@services/constants';

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