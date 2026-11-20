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
    entity-label="Ítem"
    :entry-label="treeItem.label"
    secondary-action-label="Nuevo conjunto"
    create-label="Nuevo ítem"
    :attribute-label="labelOfCode"
    @select="selectData"
    @create="createEntry"
    @secondary-action="createSet"
    @submit="submitDialog"
    @reset="resetTreeItem"
    @delete="deleteTreeItem"
    @duplicate="duplicateEntry"
  >
    <item-editor
      v-model:label="treeItem.label"
      v-model:parent="treeItem.parentId"
      v-model:attributes="attributes"
      :parent-options="parentOptions()"
      :label-rule="labelRule"
      :readonly="!canWrite"
      :attribute-entries="attributeEntries"
      :attribute-lists="attributeLists"
      :attribute-types="attributeTypes"
      :create-value="canManageConfiguration ? createAttributeValue : undefined"
    >
      <template v-slot:details-actions>
        <q-btn
          v-if="treeItem.id > 0 && canWrite"
          flat
          no-caps
          icon="add"
          label="Añadir ítem hijo"
          color="primary"
          class="q-mb-xs"
          @click="createChildEntry"
        />
      </template>
    </item-editor>

    <!--
      A set's pieces are its children: the tree already counts them, this is where they are chosen.
      Shown only for a set, because on any other item a child is a sub-item and the action for that
      is "Añadir ítem hijo" in the details header.
    -->
    <set-pieces
      v-if="isSet && treeItem.id > 0"
      :pieces="pieces"
      :candidates="candidates"
      :readonly="!canWrite"
      :busy="piecesBusy"
      :attribute-label="labelOfCode"
      @add="attachPieces"
      @remove="removePiece"
    />

    <div class="imc-section">
      <div class="imc-section__title">Adjuntos</div>
      <attachment-editor
        v-model="attachments"
        :types="attachmentTypes"
        :readonly="!canWrite"
        @add="addAttachment"
        @remove="removeAttachment"
      />
    </div>
  </tree-master-detail>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import TreeMasterDetail from '../../components/TreeMasterDetail.vue';
import ItemEditor from '../../components/ItemEditor.vue';
import SetPieces from '../../components/SetPieces.vue';
import AttachmentEditor, { type AttachmentRow } from '../../components/AttachmentEditor.vue';
import { useTreeEditor } from '../../composables/useTreeEditor';
import { useAttributeCatalogue } from '../../composables/useAttributeCatalogue';
import { useFeedback } from '../../composables/useFeedback';
import { useAuthStore } from '../../stores/auth-store';
import { itemsService, type Item, type Attributes } from '@services/items';
import { attachmentsService } from '@services/attachment';
import { constantsService, entryOf, type CatalogueEntry } from '@services/constants';
import { attributeRows, attributesJson, blankRows, readAttributeMap } from '@services/attributes';

const feedback = useFeedback();
const auth = useAuthStore();

// A lector reads; an operador writes. A superadmin impersonating somebody writes nothing.
const canWrite = computed(() => auth.canWrite);

const attributes = ref<Attributes[]>([]);
// The attribute half of Configuration is shared with the import's row correction, so it is read
// through one composable instead of two copies of the same rules.
const {
  attributes: attributeEntries,
  lists: attributeLists,
  types: attributeTypes,
  load: loadCatalogue,
  createValue: createAttributeValue,
  labelOf: labelOfCode,
  labelOfValue,
} = useAttributeCatalogue({
  onRefreshError: (error) => feedback.error(error, 'No se pudieron cargar las constantes'),
});
/** The keys an item stores, in Configuration's order. The labels never reach the payload. */
const attributeCodes = computed(() => attributeEntries.value.map((entry) => entry.code));
/** The attachment types, code + label: the select shows a label and the record keeps the code. */
const attachmentTypes = ref<CatalogueEntry[]>([]);
/** The type that opens the camera, by code: its label in Configuration can read «Foto». */
const PHOTO_TYPE_CODE = 'Photo';
const attachments = ref<AttachmentRow[]>([]);

// Only an admin and a superadmin may write Configuration, and never while impersonating.
const canManageConfiguration = computed(() => auth.canManageConfiguration);

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
  return blankRows(attributeCodes.value);
}

/**
 * The rows an entry produces, keyed by the codes it stores: the round trip through the form cannot
 * touch a key, so a label translated in Configuration leaves the record exactly as it was.
 */
function rowsFrom(raw: string | null | undefined): Attributes[] {
  const { rows, malformed } = attributeRows(raw, attributeCodes.value);
  if (malformed) {
    // Rows written before the serializer existed can hold invalid JSON; the form must still open.
    feedback.error('Este ítem tiene atributos mal formados; se reiniciaron en el formulario.');
  }
  return rows;
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
  addNew: () => ({ ...blank(), label: 'Nuevo ítem' }),
  blank,
  flatten: (nodes) => itemsService.flattenItems(nodes),
  emptyMessage: 'Todavía no hay ítems',
  deleteHint: 'Sus adjuntos se eliminan con él.',
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
        row.file ? 'picked' : '',
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
    if (attributes.value.some((row) => !row.code.trim() && row.value.trim())) {
      return 'Ponle nombre a todos los atributos';
    }
    const keys = attributes.value.map((row) => row.code.trim().toLowerCase()).filter(Boolean);
    if (new Set(keys).size !== keys.length) return 'Dos atributos tienen el mismo nombre';
    for (const attachment of attachments.value.filter((row) => row.id === 0)) {
      if (!attachment.fileName) return 'Todo adjunto nuevo necesita un archivo';
      if (!attachment.type) return 'Todo adjunto nuevo necesita un tipo';
    }
    return null;
  },
  beforeSave: (entry) => {
    // Attachments live in their own table now: a JSON payload must not carry megabytes of base64
    // for a photo that is written to disk as a file.
    entry.attachments = [];
  },
  afterSave: async (id) => {
    const pending = attachments.value.filter((row) => row.id === 0 && row.file);
    if (!pending.length || id <= 0) return;

    for (const row of pending) {
      const file = row.file;
      if (!file) continue;
      try {
        await attachmentsService.upload(id, file, row.type ?? null);
      } catch (error) {
        // One bad file must not lose the record that was just saved.
        feedback.error(error, `No se pudo subir ${row.fileName || 'el archivo'}`);
      }
    }
  },
});

/**
 * The attribute that makes an item a set, and the value that says so. Both are **codes**: the
 * labels in Configuration can read «Tipo» and «Conjunto» and nothing here notices.
 */
const TYPE_ATTRIBUTE_CODE = 'Type';
const SET_VALUE_CODE = 'Set';

/** The Type attribute of the record in the form, which is what tells a set from any other item. */
const isSet = computed(
  () =>
    attributeValue(treeItem.value.attributes, TYPE_ATTRIBUTE_CODE).trim().toLowerCase() ===
    SET_VALUE_CODE.toLowerCase(),
);

/**
 * The pieces come from the tree and not from the form's copy of the node: attaching one reloads the
 * tree, and reading it there is what keeps this list, the count badge and the form from disagreeing.
 */
const pieces = computed(() => {
  const id = treeItem.value.id;
  if (id <= 0) return [];
  return itemsService.findItem(tree.value, id)?.children ?? [];
});

/** The items this set may take: the tree's roots, minus the set itself. */
const candidates = computed(() => itemsService.rootCandidates(tree.value, treeItem.value.id));

const piecesBusy = ref(false);

/**
 * A set is created as one. Until now it took "new item" and then finding `Type` among the
 * attributes; here the name is the only question, the attribute is written with the record, and the
 * new node opens selected so its pieces are one tap away.
 */
async function createSet() {
  const label = await feedback.prompt(
    'Nuevo conjunto',
    'Nombre del conjunto',
    `Se crea con «${labelOfCode(TYPE_ATTRIBUTE_CODE)}» = «${labelOfValue(
      TYPE_ATTRIBUTE_CODE,
      SET_VALUE_CODE,
    )}». Añade sus piezas cuando esté abierto.`,
  );
  if (!label) return;

  try {
    const created = await itemsService.create({
      ...blank(),
      label,
      // The record stores the code of the value, never its label.
      attributes: JSON.stringify({ [TYPE_ATTRIBUTE_CODE]: SET_VALUE_CODE }),
    });
    await loadData();
    // The list and the panels are the server's answer, so the new node is found there, not here.
    const node =
      (created?.id ? itemsService.findItem(tree.value, created.id) : undefined) ??
      tree.value.find((candidate) => candidate.label === label);
    if (!node) {
      feedback.error('El conjunto se creó, pero el árbol no lo devolvió; recarga para verlo.');
      return;
    }
    selected.value = node.id;
    await selectData(node.id);
    feedback.success('Conjunto creado');
  } catch (error) {
    feedback.error(error, 'No se pudo crear el conjunto');
  }
}

/** Attaches the picked items in one call, then reloads so the tree's count follows. */
async function attachPieces(ids: number[]) {
  const setId = treeItem.value.id;
  if (setId <= 0 || ids.length === 0) return;

  piecesBusy.value = true;
  try {
    const result = await itemsService.attachChildren(setId, ids);
    feedback.success(`${result.moved} ${result.moved === 1 ? 'pieza añadida' : 'piezas añadidas'}`);
    await loadData();
  } catch (error) {
    feedback.error(error, 'No se pudieron añadir las piezas');
  } finally {
    piecesBusy.value = false;
  }
}

/** Detaches one piece: it leaves the set and stays in the inventory. */
async function removePiece(childId: number) {
  const setId = treeItem.value.id;
  if (setId <= 0 || childId <= 0) return;

  piecesBusy.value = true;
  try {
    await itemsService.detachChild(setId, childId);
    feedback.success('Pieza quitada del conjunto');
    await loadData();
  } catch (error) {
    feedback.error(error, 'No se pudo quitar la pieza');
  } finally {
    piecesBusy.value = false;
  }
}

/**
 * Reads what the form is built from: the attribute catalogue — shared with the import's row
 * correction, which edits an item with the same fields — and the attachment types, which only this
 * screen needs.
 */
async function loadConstants() {
  try {
    const [, attachmentTypeRows] = await Promise.all([
      loadCatalogue(),
      constantsService.getByGroup('AttachmentType'),
    ]);
    attachmentTypes.value = (attachmentTypeRows ?? []).map(entryOf);
    // The catalogue names the rows of a blank item; a later refresh (a value added from the form)
    // must not go through here, or it would wipe what the user already typed.
    attributes.value = blankAttributes();
  } catch (error) {
    feedback.error(error, 'No se pudieron cargar las constantes');
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
    feedback.error(error, 'No se pudieron cargar los adjuntos');
  }
}

/** One attribute of a record, by code, for the decisions this page makes outside the form. */
function attributeValue(raw: string | null | undefined, code: string): string {
  return readAttributeMap(raw)?.[code] ?? '';
}

function addAttachment() {
  attachments.value.push({
    id: 0,
    itemId: treeItem.value.id,
    // Photos are the common case on a phone, and the type decides which picker opens. The default
    // is the type's code: «Foto» in Configuration would still store `Photo`.
    type: attachmentTypes.value.some((entry) => entry.code === PHOTO_TYPE_CODE)
      ? PHOTO_TYPE_CODE
      : null,
    fileName: null,
    file: null,
    previewUrl: null,
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
    `¿Eliminar el adjunto «${row.fileName || row.id}»? No se puede deshacer.`,
  );
  if (!confirmed) return;
  try {
    await attachmentsService.delete(row.id);
    feedback.success('Adjunto eliminado');
    await loadAttachments(row.itemId);
  } catch (error) {
    feedback.error(error, 'No se pudo eliminar el adjunto');
  }
}

watch(
  attributes,
  () => {
    // The payload is built from codes, so a label translated in Configuration moves nothing here.
    treeItem.value.attributes = attributesJson(attributes.value);
  },
  { deep: true },
);
</script>
