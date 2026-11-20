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
    entity-label="Constante"
    :entry-label="treeItem.label"
    create-label="Nueva constante"
    @select="selectData"
    @create="createEntry"
    @submit="submitDialog"
    @reset="resetTreeItem"
    @delete="deleteTreeItem"
    @duplicate="duplicateEntry"
  >
    <div class="imc-section">
      <div class="row items-center justify-between q-mb-xs">
        <div class="imc-section__title q-mb-none">Detalles</div>
        <q-btn
          v-if="treeItem.id > 0 && canWrite"
          flat
          no-caps
          icon="add"
          label="Añadir constante hija"
          color="primary"
          class="q-mb-xs"
          @click="createChildEntry"
        />
      </div>
      <q-input label="Nombre" v-model="treeItem.label" :readonly="!canWrite" :rules="[labelRule]" />
      <q-select
        label="Padre"
        v-model="treeItem.parentId"
        :options="parentOptions()"
        :display-value="treeItem.parentId == null ? 'Sin padre' : undefined"
        :readonly="!canWrite"
        map-options
        emit-value
        class="q-mt-sm"
      />
    </div>

    <div class="imc-section">
      <div class="imc-section__title">Definición</div>
      <q-input label="Grupo" v-model="treeItem.group" :readonly="!canWrite" />
      <q-input
        dense
        label="Descripción"
        v-model="treeItem.description"
        :readonly="!canWrite"
        type="textarea"
        autogrow
        class="q-mt-md"
      />
      <q-select
        v-if="isAttributeDefinition"
        dense
        label="Tipo de dato"
        v-model="treeItem.dataType"
        :options="dataTypes"
        :hint="typeHint"
        :readonly="!canWrite"
        map-options
        emit-value
        class="q-mt-md"
      />
      <q-input label="Orden" v-model="treeItem.sortOrder" :readonly="!canWrite" class="q-mt-md" />
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

/** The stored values are the API's vocabulary; only what the user reads is Spanish. */
const dataTypes = [
  { label: 'Texto', value: 'Text' },
  { label: 'Número', value: 'Number' },
  { label: 'Importe', value: 'Money' },
  { label: 'Fecha', value: 'Date' },
  { label: 'Texto largo', value: 'LongText' },
];

/**
 * Only a child of the Attribute group describes an item field, so only it has a data type. The
 * group is an identity, not a word on the screen: Configuration can call that node «Atributo».
 */
const ATTRIBUTE_GROUP_CODE = 'Attribute';
const isAttributeDefinition = computed(
  () => treeItem.value.group === ATTRIBUTE_GROUP_CODE && (treeItem.value.parentId ?? 0) > 0,
);

/** The stored data type, not its label: the select shows «Importe» and the value is `Money`. */
const typeHint = computed(() => {
  switch (treeItem.value.dataType) {
    case 'Money':
      return 'Números enteros, con separadores y teclado decimal en el móvil.';
    case 'Number':
      return 'Dígitos y un separador decimal.';
    case 'Date':
      return 'Se elige en el calendario en vez de escribirse.';
    case 'LongText':
      return 'Un párrafo, editado en un cuadro de texto.';
    default:
      return 'Cualquier texto, tal cual se escribe.';
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
  addNew: () => ({ ...blank(), label: 'Nueva constante' }),
  blank,
  flatten: (nodes) => constantsService.flattenConstants(nodes),
  emptyMessage: 'Todavía no hay constantes',
  deleteHint: 'Sus constantes hijas se eliminan con ella.',
  splitter: 30,
  // Only the entry: a constant is a name, an icon and a parent.
  dirtySnapshot: (entry) => JSON.stringify(entry),
});

onMounted(() => {
  void loadData();
});
</script>
