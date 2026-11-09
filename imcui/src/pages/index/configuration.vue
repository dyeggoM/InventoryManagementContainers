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
    <q-input label="group" v-model="treeItem.group" />
    <q-input label="description" v-model="treeItem.description" type="textarea" />
    <q-input label="sortOrder" v-model="treeItem.sortOrder" />
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
  emptyMessage,
  labelRule,
  loadData,
  selectData,
  resetTreeItem,
  submitDialog,
  deleteTreeItem,
  parentOptions,
} = useTreeEditor<Constants>({
  service: constantsService,
  addNew: () => ({ ...blank(), label: 'Add New' }),
  blank,
  flatten: (nodes) => constantsService.flattenConstants(nodes),
  entity: 'constant',
  emptyMessage: 'No constants yet — use the "Add New" node to create the first one.',
  deleteHint: 'Its children are deleted with it.',
  splitter: 30,
});

onMounted(() => {
  void loadData();
});
</script>
